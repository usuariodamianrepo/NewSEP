namespace SEP.WebApiEtikit.Labeling
{
    public enum FontRendering
    {
        Bitmap,
        Outline
    }

    public abstract class LabelElement
    {
        public string Id { get; set; } = string.Empty;

        public double X { get; set; }

        public double Y { get; set; }

        /// <summary>
        /// Rotation expressed in quarter turns (0 = 0°, 1 = 90°, 2 = 180°, 3 = 270°).
        /// </summary>
        public int Rotation { get; set; }

        public bool Reverse { get; set; }
    }

    public sealed class TextElement : LabelElement
    {
        public string? Content { get; set; }

        public string FontCode { get; set; } = "0";

        public double Width { get; set; }

        public double Height { get; set; }

        public FontRendering? FontRendering { get; set; }

        public bool InlineText { get; set; }

        public double? ZplFieldOriginAscentRatio { get; set; }
    }

    public sealed class BarcodeElement : LabelElement
    {
        public string? Content { get; set; }

        /// <summary>
        /// Lower-case symbology name: code128, code39, ean13, ean8, upca or upce.
        /// </summary>
        public string BarcodeType { get; set; } = "code128";

        public double Height { get; set; }

        /// <summary>
        /// Narrow bar width. ZPL: dots. TPCL: base dots (203 dpi).
        /// </summary>
        public double Width { get; set; }

        public double? Ratio { get; set; }

        public bool ShowText { get; set; }
    }

    public sealed class QrCodeElement : LabelElement
    {
        public string? Content { get; set; }

        /// <summary>
        /// Module size. ZPL: dots. TPCL: base dots (203 dpi).
        /// </summary>
        public double Size { get; set; }

        public string ErrorCorrection { get; set; } = "H";

        public string QrModel { get; set; } = "M1";
    }

    public sealed class LineElement : LabelElement
    {
        public double X2 { get; set; }

        public double Y2 { get; set; }

        /// <summary>
        /// Thickness. ZPL: dots. TPCL: base dots (203 dpi).
        /// </summary>
        public double Thickness { get; set; }
    }

    public sealed class RectangleElement : LabelElement
    {
        public double Width { get; set; }

        public double Height { get; set; }

        /// <summary>
        /// Border thickness. ZPL: dots. TPCL: base dots (203 dpi).
        /// </summary>
        public double Thickness { get; set; }

        public bool Filled { get; set; }
    }
}
