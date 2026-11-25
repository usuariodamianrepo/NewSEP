namespace SEP.WebApi.Services
{
    public sealed class ZplRenderOptions
    {
        public double? WidthMm { get; set; }

        public double? HeightMm { get; set; }

        public double? Dpmm { get; set; }

        public bool GrayscaleOutput { get; set; }

        public bool EnableInvertedLabels { get; set; }
    }
}
