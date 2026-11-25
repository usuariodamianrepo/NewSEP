namespace SEP.WebApi.Services
{
    public interface IZplRenderer
    {
        Task<byte[]> RenderAsync(
            string zpl,
            ZplRenderOptions? options = null,
            CancellationToken cancellationToken = default);
    }
}
