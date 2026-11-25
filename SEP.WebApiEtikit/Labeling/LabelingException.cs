namespace SEP.WebApiEtikit.Labeling
{
    public sealed class LabelParseException : Exception
    {
        public LabelParseException(string message)
            : base(message)
        {
        }

        public LabelParseException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public sealed class LabelRenderException : Exception
    {
        public LabelRenderException(string message)
            : base(message)
        {
        }

        public LabelRenderException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
