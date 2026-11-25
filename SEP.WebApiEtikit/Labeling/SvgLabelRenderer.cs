using System.Text;
using System.Xml;

namespace SEP.WebApiEtikit.Labeling
{
    public sealed class SvgRenderOptions
    {
        public bool IncludeBackground { get; set; } = true;

        public string BackgroundColor { get; set; } = "#FFFFFF";
    }

    /// <summary>
    /// Renders etikit's intermediate label model as an SVG document.
    /// All coordinates in the output use millimetres as user units.
    /// </summary>
    public sealed class SvgLabelRenderer
    {
        private const string SvgNamespace = "http://www.w3.org/2000/svg";
        private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
        private const double TpclSymbolModuleScale = 2.0 / 3.0;

        public string Render(LabelDocument document, SvgRenderOptions? options = null)
        {
            options ??= new SvgRenderOptions();

            var width = document.WidthMm > 0 ? document.WidthMm : 100;
            var height = document.HeightMm > 0 ? document.HeightMm : 150;

            var builder = new StringBuilder();
            using (var writer = XmlWriter.Create(builder, new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent = false,
                ConformanceLevel = ConformanceLevel.Document
            }))
            {
                writer.WriteStartElement("svg", SvgNamespace);
                writer.WriteAttributeString("width", $"{LabelMath.Format(width)}mm");
                writer.WriteAttributeString("height", $"{LabelMath.Format(height)}mm");
                writer.WriteAttributeString("viewBox", $"0 0 {LabelMath.Format(width)} {LabelMath.Format(height)}");
                writer.WriteAttributeString("preserveAspectRatio", "xMinYMin meet");

                if (options.IncludeBackground)
                {
                    Start(writer, "rect");
                    writer.WriteAttributeString("x", "0");
                    writer.WriteAttributeString("y", "0");
                    writer.WriteAttributeString("width", LabelMath.Format(width));
                    writer.WriteAttributeString("height", LabelMath.Format(height));
                    writer.WriteAttributeString("fill", options.BackgroundColor);
                    writer.WriteEndElement();
                }

                foreach (var element in document.Elements)
                {
                    RenderElement(writer, element, document);
                }

                writer.WriteEndElement();
            }

            return builder.ToString();
        }

        private static void RenderElement(XmlWriter writer, LabelElement element, LabelDocument document)
        {
            switch (element)
            {
                case TextElement text:
                    RenderText(writer, text, document);
                    break;
                case BarcodeElement barcode:
                    RenderBarcode(writer, barcode, document);
                    break;
                case QrCodeElement qr:
                    RenderQrCode(writer, qr, document);
                    break;
                case LineElement line:
                    RenderLine(writer, line, document);
                    break;
                case RectangleElement rectangle:
                    RenderRectangle(writer, rectangle, document);
                    break;
            }
        }

        private static void RenderText(XmlWriter writer, TextElement element, LabelDocument document)
        {
            if (string.IsNullOrEmpty(element.Content))
            {
                return;
            }

            var font = LabelFontCatalog.Resolve(element.FontCode, document.Protocol);
            var fontSizeMm = document.Protocol == LabelProtocol.Zpl
                ? UnitsToMm(Math.Max(1, element.Height), document)
                : font.FontSizePt * 25.4 / 72.0;

            var (scaleX, scaleY) = GetTextScales(element, document.Protocol);

            double originX;
            double originY;

            if (document.Protocol == LabelProtocol.Zpl)
            {
                var ascentRatio = element.ZplFieldOriginAscentRatio ?? 0.8;
                originX = UnitsToMm(element.X, document);
                originY = UnitsToMm(element.Y - element.Height * ascentRatio, document);
            }
            else
            {
                originX = UnitsToMm(element.X, document);
                originY = UnitsToMm(element.Y, document);
            }

            StartGroup(writer, originX, originY, element.Rotation);

            Start(writer, "g");
            writer.WriteAttributeString("transform", $"scale({LabelMath.Format(scaleX)} {LabelMath.Format(scaleY)})");

            Start(writer, "text");
            writer.WriteAttributeString("x", "0");
            writer.WriteAttributeString("y", LabelMath.Format(fontSizeMm * 0.8));
            writer.WriteAttributeString("font-family", LabelFontCatalog.GetCssFontFamily(font.FontFamily));
            writer.WriteAttributeString("font-size", LabelMath.Format(fontSizeMm));
            writer.WriteAttributeString("font-weight", font.FontWeight);
            writer.WriteAttributeString("font-style", font.FontStyle);
            writer.WriteAttributeString("fill", GetColor(element.Reverse));
            writer.WriteAttributeString("xml", "space", XmlNamespace, "preserve");

            var lines = SplitLines(element.Content);
            for (var index = 0; index < lines.Length; index++)
            {
                Start(writer, "tspan");
                writer.WriteAttributeString("x", "0");
                if (index > 0)
                {
                    writer.WriteAttributeString("dy", LabelMath.Format(fontSizeMm));
                }

                writer.WriteString(lines[index]);
                writer.WriteEndElement();
            }

            writer.WriteEndElement(); // text
            writer.WriteEndElement(); // g (scale)
            writer.WriteEndElement(); // g (translate/rotate)
        }

        private static void RenderLine(XmlWriter writer, LineElement element, LabelDocument document)
        {
            var x = UnitsToMm(element.X, document);
            var y = UnitsToMm(element.Y, document);
            var x2 = UnitsToMm(element.X2, document);
            var y2 = UnitsToMm(element.Y2, document);
            var thickness = Math.Max(0.05, ThicknessToMm(element.Thickness, document));

            StartGroup(writer, x, y, element.Rotation);

            Start(writer, "line");
            writer.WriteAttributeString("x1", "0");
            writer.WriteAttributeString("y1", "0");
            writer.WriteAttributeString("x2", LabelMath.Format(x2 - x));
            writer.WriteAttributeString("y2", LabelMath.Format(y2 - y));
            writer.WriteAttributeString("stroke", GetColor(element.Reverse));
            writer.WriteAttributeString("stroke-width", LabelMath.Format(thickness));
            writer.WriteAttributeString("stroke-linecap", "square");
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        private static void RenderRectangle(XmlWriter writer, RectangleElement element, LabelDocument document)
        {
            var x = UnitsToMm(element.X, document);
            var y = UnitsToMm(element.Y, document);
            var width = Math.Max(0, UnitsToMm(element.Width, document));
            var height = Math.Max(0, UnitsToMm(element.Height, document));
            var thickness = Math.Max(0.05, ThicknessToMm(element.Thickness, document));

            StartGroup(writer, x, y, element.Rotation);

            Start(writer, "rect");
            writer.WriteAttributeString("x", "0");
            writer.WriteAttributeString("y", "0");
            writer.WriteAttributeString("width", LabelMath.Format(width));
            writer.WriteAttributeString("height", LabelMath.Format(height));

            if (element.Filled)
            {
                writer.WriteAttributeString("fill", GetColor(element.Reverse));
                writer.WriteAttributeString("stroke", "none");
            }
            else
            {
                writer.WriteAttributeString("fill", "none");
                writer.WriteAttributeString("stroke", GetColor(element.Reverse));
                writer.WriteAttributeString("stroke-width", LabelMath.Format(thickness));
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        private static void RenderBarcode(XmlWriter writer, BarcodeElement element, LabelDocument document)
        {
            var matrix = BarcodeSvgFactory.TryEncode(element.Content, element.BarcodeType);
            if (matrix is null)
            {
                return;
            }

            var narrowBarMm = document.Protocol == LabelProtocol.Zpl
                ? UnitsToMm(Math.Max(1, element.Width), document)
                : BaseDotsToMm(Math.Max(1, element.Width)) * TpclSymbolModuleScale;
            var barHeightMm = document.Protocol == LabelProtocol.Zpl
                ? UnitsToMm(Math.Max(1, element.Height), document)
                : BaseDotsToMm(Math.Max(1, element.Height));

            var targetWidthMm = matrix.Width * narrowBarMm;
            var targetHeightMm = barHeightMm;

            var x = UnitsToMm(element.X, document);
            var y = UnitsToMm(element.Y, document);

            StartGroup(writer, x, y, element.Rotation);
            WriteMatrix(writer, matrix, targetWidthMm, targetHeightMm, element.Reverse);

            if (element.ShowText && !string.IsNullOrEmpty(element.Content))
            {
                var fontSizeMm = Math.Max(1.2, targetHeightMm * 0.22);
                Start(writer, "text");
                writer.WriteAttributeString("x", LabelMath.Format(targetWidthMm / 2));
                writer.WriteAttributeString("y", LabelMath.Format(targetHeightMm + fontSizeMm));
                writer.WriteAttributeString("font-family", LabelFontCatalog.GetCssFontFamily("courier"));
                writer.WriteAttributeString("font-size", LabelMath.Format(fontSizeMm));
                writer.WriteAttributeString("text-anchor", "middle");
                writer.WriteAttributeString("fill", GetColor(element.Reverse));
                writer.WriteString(element.Content);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static void RenderQrCode(XmlWriter writer, QrCodeElement element, LabelDocument document)
        {
            var matrix = BarcodeSvgFactory.TryEncode(element.Content, "qrcode", element.ErrorCorrection);
            if (matrix is null)
            {
                return;
            }

            var moduleSizeMm = document.Protocol == LabelProtocol.Zpl
                ? UnitsToMm(Math.Max(1, element.Size), document)
                : BaseDotsToMm(Math.Max(1, element.Size)) * TpclSymbolModuleScale;

            var sizeMm = matrix.Width * moduleSizeMm;
            var x = UnitsToMm(element.X, document);
            var y = UnitsToMm(element.Y, document);

            StartGroup(writer, x, y, element.Rotation);
            WriteMatrix(writer, matrix, sizeMm, sizeMm, element.Reverse);
            writer.WriteEndElement();
        }

        private static void WriteMatrix(
            XmlWriter writer,
            BarcodeMatrix matrix,
            double widthMm,
            double heightMm,
            bool reverse)
        {
            Start(writer, "svg");
            writer.WriteAttributeString("x", "0");
            writer.WriteAttributeString("y", "0");
            writer.WriteAttributeString("width", LabelMath.Format(widthMm));
            writer.WriteAttributeString("height", LabelMath.Format(heightMm));
            writer.WriteAttributeString("viewBox", $"0 0 {matrix.Width} {matrix.Height}");
            writer.WriteAttributeString("preserveAspectRatio", "none");

            Start(writer, "path");
            writer.WriteAttributeString("d", matrix.PathData);
            writer.WriteAttributeString("fill", GetColor(reverse));
            writer.WriteAttributeString("shape-rendering", "crispEdges");
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        private static void StartGroup(XmlWriter writer, double x, double y, int rotation)
        {
            Start(writer, "g");
            writer.WriteAttributeString(
                "transform",
                $"translate({LabelMath.Format(x)} {LabelMath.Format(y)}) rotate({rotation * 90})");
        }

        private static void Start(XmlWriter writer, string localName) =>
            writer.WriteStartElement(localName, SvgNamespace);

        private static string GetColor(bool reverse) => reverse ? "white" : "black";

        private static string[] SplitLines(string content) =>
            content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

        private static (double X, double Y) GetTextScales(TextElement element, LabelProtocol protocol)
        {
            if (protocol == LabelProtocol.Zpl)
            {
                var widthDots = element.Width > 0 ? element.Width : 0;
                var heightDots = element.Height > 0 ? element.Height : 0;
                var fontWidthCorrection = element.FontCode == "0" ? 0.8 : 1.0;
                var bitmapFontWidthCompensation = element.FontCode == "0" ? 1.0 : 1.0 / 0.6;
                var scaleX = (widthDots > 0 && heightDots > 0 ? widthDots / heightDots : 1.0)
                             * fontWidthCorrection
                             * bitmapFontWidthCompensation;

                return (scaleX, 1.0);
            }

            return (
                NormalizeTextScale(element.Width > 0 ? element.Width : 10),
                NormalizeTextScale(element.Height > 0 ? element.Height : 10));
        }

        private static double NormalizeTextScale(double value) =>
            value >= 10 ? value / 10 : value <= 0 ? 1 : value;

        private static double UnitsToMm(double value, LabelDocument document) =>
            value / UnitsPerMm(document);

        private static double UnitsPerMm(LabelDocument document) =>
            document.Protocol == LabelProtocol.Zpl
                ? (document.Dpi > 0 ? document.Dpi : 203) / 25.4
                : 10.0;

        private static double BaseDotsToMm(double value) => value / 8.0;

        private static double ThicknessToMm(double value, LabelDocument document) =>
            document.Protocol == LabelProtocol.Zpl
                ? UnitsToMm(value, document)
                : BaseDotsToMm(value);
    }
}
