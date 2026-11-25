namespace SEP.WebApi.Services
{
    public sealed class ZplRendererException : Exception
    {
        public ZplRendererException(string message)
            : base(message)
        {
        }

        public ZplRendererException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
