namespace SEP.WebApi.Services
{
    public sealed class TpclConversionException : Exception
    {
        public TpclConversionException(string message)
            : base(message)
        {
        }
    }
}
