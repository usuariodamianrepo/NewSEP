using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SEP.WebApi.Services
{
    /// <summary>
    /// Convierte el subconjunto de TPCL usado por el proyecto ({D}, {PV}, {LC}, {XB})
    /// a ZPL, para poder reutilizar el renderer de etiquetas ya existente.
    /// </summary>
    public static class TpclToZplConverter
    {
        private static readonly Regex CommandRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

        public static (string Zpl, ZplRenderOptions Options) Convert(string tpclCode)
        {
            var options = new ZplRenderOptions();
            var zpl = new StringBuilder("^XA");

            foreach (Match match in CommandRegex.Matches(tpclCode))
            {
                var command = match.Groups[1].Value.Trim().TrimEnd('|');
                if (command.Length == 0)
                {
                    continue;
                }

                if (command.StartsWith('D') && !command.Contains(';'))
                {
                    ApplyDimensions(command, options);
                }
                else if (command.StartsWith("PV", StringComparison.Ordinal))
                {
                    zpl.Append(ConvertText(command));
                }
                else if (command.StartsWith("LC", StringComparison.Ordinal))
                {
                    zpl.Append(ConvertLine(command));
                }
                else if (command.StartsWith("XB", StringComparison.Ordinal))
                {
                    zpl.Append(ConvertBarcode(command));
                }
                else if (command == "C" || command.StartsWith("XS", StringComparison.Ordinal))
                {
                    // Configuración y orden de impresión: no dibujan nada.
                }
                else
                {
                    throw new TpclConversionException($"Comando TPCL no soportado: '{command}'.");
                }
            }

            return (zpl.Append("^XZ").ToString(), options);
        }

        private static void ApplyDimensions(string command, ZplRenderOptions options)
        {
            var values = command[1..].Split(',');
            if (values.Length < 2)
            {
                throw new TpclConversionException("Comando TPCL {D} incompleto.");
            }

            // TPCL expresa el tamaño en décimas de milímetro.
            options.WidthMm = Parse(values[0]) / 10.0;
            options.HeightMm = Parse(values[1]) / 10.0;

            // Las coordenadas TPCL son puntos a 203 dpi (aprox. 8 puntos/mm).
            options.Dpmm = 8;
        }

        private static string ConvertText(string command)
        {
            var values = ValuesOf(command);
            if (values.Length < 4)
            {
                throw new TpclConversionException("Comando TPCL {PV} incompleto.");
            }

            var text = ExtractData(values);

            return $"^FO{Parse(values[0])},{Parse(values[1])}" +
                   $"^A0N,{Parse(values[3])},{Parse(values[2])}^FD{text}^FS";
        }

        private static string ConvertLine(string command)
        {
            var values = ValuesOf(command);
            if (values.Length < 6)
            {
                throw new TpclConversionException("Comando TPCL {LC} incompleto.");
            }

            var x1 = Parse(values[0]);
            var y1 = Parse(values[1]);
            var width = Math.Max(1, Math.Abs(Parse(values[2]) - x1));
            var height = Math.Max(1, Math.Abs(Parse(values[3]) - y1));
            var thickness = Parse(values[5]);

            return $"^FO{x1},{y1}^GB{width},{height},{thickness}^FS";
        }

        private static string ConvertBarcode(string command)
        {
            var values = ValuesOf(command);
            if (values.Length < 7)
            {
                throw new TpclConversionException("Comando TPCL {XB} incompleto.");
            }

            if (values[2] != "5")
            {
                throw new TpclConversionException(
                    $"Tipo de código de barras TPCL no soportado: '{values[2]}'.");
            }

            var text = ExtractData(values);

            return $"^FO{Parse(values[0])},{Parse(values[1])}" +
                   $"^BCN,{Parse(values[6])},Y,N,N^FD{text}^FS";
        }

        private static string ExtractData(string[] values)
        {
            var data = values.FirstOrDefault(value => value.Contains('='));
            var text = data?[(data.IndexOf('=') + 1)..] ?? string.Empty;

            return text.Trim('[', ']');
        }

        private static string[] ValuesOf(string command)
        {
            var semicolon = command.IndexOf(';');

            return semicolon < 0
                ? Array.Empty<string>()
                : command[(semicolon + 1)..].Split(',');
        }

        private static int Parse(string value) =>
            int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}
