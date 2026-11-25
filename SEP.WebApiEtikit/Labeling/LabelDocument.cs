namespace SEP.WebApiEtikit.Labeling
{
    public enum LabelProtocol
    {
        Zpl,
        Tpcl
    }

    public sealed class LabelDocument
    {
        public string Name { get; set; } = string.Empty;

        public double WidthMm { get; set; }

        public double HeightMm { get; set; }

        public double Dpi { get; set; } = 203;

        public LabelProtocol Protocol { get; set; }

        public int Quantity { get; set; } = 1;

        public int? Speed { get; set; }

        public int? Darkness { get; set; }

        public List<LabelElement> Elements { get; set; } = new();
    }
}
