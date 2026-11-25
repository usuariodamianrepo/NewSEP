using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SEP.WebApi.Services
{
    /// <summary>
    /// Keeps a single long-lived Node process running zpl-renderer-js and
    /// correlates requests/responses over stdin/stdout.
    /// </summary>
    public sealed class ZplRenderer : IZplRenderer, IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private readonly string _nodePath;
        private readonly string _scriptPath;
        private readonly TimeSpan _timeout;
        private readonly ILogger<ZplRenderer> _logger;
        private readonly object _sync = new();
        private readonly Dictionary<int, TaskCompletionSource<ZplRenderResponse>> _pending = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        private Process? _process;
        private int _nextId;
        private bool _disposed;

        public ZplRenderer(ILogger<ZplRenderer> logger, IConfiguration configuration)
        {
            _logger = logger;
            _nodePath = configuration["ZplRenderer:NodePath"] ?? "node";

            var configuredScriptPath = configuration["ZplRenderer:ScriptPath"] ?? "ZplRenderer/render.cjs";
            _scriptPath = ResolveScriptPath(configuredScriptPath);

            var timeoutSeconds = configuration.GetValue<double?>("ZplRenderer:TimeoutSeconds") ?? 30;
            _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        public async Task<byte[]> RenderAsync(
            string zpl,
            ZplRenderOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var process = await EnsureStartedAsync().ConfigureAwait(false);

            TaskCompletionSource<ZplRenderResponse> completion;
            int id;
            lock (_sync)
            {
                id = ++_nextId;
                completion = new TaskCompletionSource<ZplRenderResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[id] = completion;
            }

            var request = new ZplRenderRequest(
                id,
                zpl,
                options?.WidthMm,
                options?.HeightMm,
                options?.Dpmm,
                options?.GrayscaleOutput ?? false,
                options?.EnableInvertedLabels ?? false);

            using var timeoutCts = new CancellationTokenSource(_timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _writeLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                try
                {
                    if (process.HasExited)
                    {
                        throw new ZplRendererException("El proceso Node del renderer ZPL no está activo.");
                    }

                    var json = JsonSerializer.Serialize(request, JsonOptions);
                    await process.StandardInput.WriteLineAsync(json).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync().ConfigureAwait(false);
                }
                finally
                {
                    _writeLock.Release();
                }

                var response = await completion.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(response.Error))
                {
                    throw new ZplRendererException(response.Error);
                }

                if (string.IsNullOrEmpty(response.PngBase64))
                {
                    throw new ZplRendererException("El renderer ZPL no devolvió una imagen.");
                }

                return Convert.FromBase64String(response.PngBase64);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new ZplRendererException(
                    $"Timeout de {_timeout.TotalSeconds:0.#} segundos renderizando la etiqueta ZPL.");
            }
            catch (Exception exception) when (exception is not ZplRendererException)
            {
                throw new ZplRendererException("Error al comunicarse con el renderer ZPL.", exception);
            }
            finally
            {
                lock (_sync)
                {
                    _pending.Remove(id);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            Process? process;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                process = _process;
                _process = null;
            }

            FailAllPending("El renderer ZPL se está apagando.");

            if (process is null)
            {
                return;
            }

            try
            {
                process.StandardInput.Close();

                if (!process.HasExited)
                {
                    var exitTask = process.WaitForExitAsync();
                    var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                    if (completed != exitTask)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    else
                    {
                        await exitTask.ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Error al cerrar el proceso del renderer ZPL.");
            }
            finally
            {
                process.Dispose();
            }
        }

        private async Task<Process> EnsureStartedAsync()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(ZplRenderer));
                }

                if (_process is { HasExited: false })
                {
                    return _process;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _nodePath,
                    WorkingDirectory = Path.GetDirectoryName(_scriptPath)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                startInfo.ArgumentList.Add(_scriptPath);

                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.Exited += (_, _) => OnProcessExited(process);

                try
                {
                    if (!process.Start())
                    {
                        throw new ZplRendererException("No se pudo iniciar el proceso Node del renderer ZPL.");
                    }
                }
                catch (Exception exception)
                {
                    process.Dispose();
                    throw new ZplRendererException(
                        $"No se pudo iniciar Node ('{_nodePath}'). Comprueba que Node esté instalado y en el PATH.",
                        exception);
                }

                _process = process;
                _ = Task.Run(() => ReadStdoutAsync(process));
                _ = WatchStderrAsync(process);
                _logger.LogInformation("Renderer ZPL iniciado ({ScriptPath}).", _scriptPath);

                return process;
            }
        }

        private async Task ReadStdoutAsync(Process process)
        {
            try
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    ZplRenderResponse? response;
                    try
                    {
                        response = JsonSerializer.Deserialize<ZplRenderResponse>(line, JsonOptions);
                    }
                    catch (JsonException exception)
                    {
                        _logger.LogWarning(exception, "Respuesta inválida del renderer ZPL: {Line}", line);
                        continue;
                    }

                    if (response is null)
                    {
                        continue;
                    }

                    lock (_sync)
                    {
                        if (_pending.Remove(response.Id, out var completion))
                        {
                            completion.TrySetResult(response);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Se terminó la lectura de stdout del renderer ZPL.");
            }
            finally
            {
                FailAllPending("El renderer ZPL cerró su salida antes de responder.");
            }
        }

        private async Task WatchStderrAsync(Process process)
        {
            try
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _logger.LogWarning("ZPL renderer (node): {Message}", line);
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Se terminó la lectura de stderr del renderer ZPL.");
            }
        }

        private void OnProcessExited(Process process)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }

            FailAllPending("El proceso Node del renderer ZPL finalizó inesperadamente.");
            process.Dispose();
        }

        private void FailAllPending(string message)
        {
            TaskCompletionSource<ZplRenderResponse>[] pending;
            lock (_sync)
            {
                pending = _pending.Values.ToArray();
                _pending.Clear();
            }

            foreach (var completion in pending)
            {
                completion.TrySetException(new ZplRendererException(message));
            }
        }

        private static string ResolveScriptPath(string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory is not null)
                {
                    var candidate = Path.Combine(directory.FullName, configuredPath);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    directory = directory.Parent;
                }
            }

            return Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
        }

        private sealed record ZplRenderRequest(
            int Id,
            string Zpl,
            double? WidthMm,
            double? HeightMm,
            double? Dpmm,
            bool GrayscaleOutput,
            bool EnableInvertedLabels);

        private sealed record ZplRenderResponse(int Id, string? PngBase64, string? Error);
    }
}
