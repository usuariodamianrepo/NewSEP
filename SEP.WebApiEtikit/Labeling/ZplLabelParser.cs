using System.Globalization;
using System.Text;

namespace SEP.WebApiEtikit.Labeling
{
    /// <summary>
    /// Port of etikit's ZPLDriver.parse (src/drivers/zpl.ts).
    /// Produces the same intermediate label model used by the SVG renderer.
    /// </summary>
    public sealed class ZplLabelParser
    {
        private static readonly Encoding Cp850 = CreateCp850();

        private static Encoding CreateCp850()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(850);
        }

        private static readonly Dictionary<string, double> BitmapFontWidths = new()
        {
            ["A"] = 5.0 / 9,
            ["B"] = 7.0 / 11,
            ["C"] = 10.0 / 18,
            ["D"] = 10.0 / 18,
            ["E"] = 15.0 / 28,
            ["F"] = 13.0 / 26,
            ["G"] = 40.0 / 60,
            ["H"] = 13.0 / 21
        };

        public LabelDocument Parse(string zpl)
        {
            if (string.IsNullOrWhiteSpace(zpl))
            {
                throw new LabelParseException("El código ZPL está vacío.");
            }

            var start = zpl.IndexOf("^XA", StringComparison.Ordinal);
            var end = zpl.LastIndexOf("^XZ", StringComparison.Ordinal);
            var content = start >= 0 && end > start ? zpl[start..(end + 3)] : zpl;

            var fields = new List<ZplField>();
            double maxXDots = 0;
            double maxYDots = 0;
            bool hasPW = false;
            bool hasLL = false;
            double? pwDots = null;
            double? llDots = null;
            int quantity = 1;
            int? speed = null;
            int? darkness = null;

            FieldOrigin? currentFO = null;
            ByState? currentBy = null;
            var currentFont = new FontState("A", 9, 5);
            var reverseNextField = false;
            PendingField? pending = null;
            string? pendingData = null;
            char? hexIndicator = null;

            void UpdateMax(double xDots, double yDots, double wDots, double hDots)
            {
                maxXDots = Math.Max(maxXDots, Math.Max(0, xDots) + Math.Max(0, wDots));
                maxYDots = Math.Max(maxYDots, Math.Max(0, yDots) + Math.Max(0, hDots));
            }

            foreach (var raw in content.Split('^'))
            {
                if (raw.Length == 0)
                {
                    continue;
                }

                var cmd = raw.Length >= 2 ? raw[..2] : raw;
                var rest = raw.Length > 2 ? raw[2..] : string.Empty;

                if (cmd == "PW")
                {
                    var dots = ParseInt(rest);
                    if (dots.HasValue)
                    {
                        pwDots = dots.Value;
                        hasPW = true;
                    }
                    continue;
                }

                if (cmd == "LL")
                {
                    var dots = ParseInt(rest);
                    if (dots.HasValue)
                    {
                        llDots = dots.Value;
                        hasLL = true;
                    }
                    continue;
                }

                if (cmd == "MD")
                {
                    var value = ParseInt(rest);
                    if (value.HasValue)
                    {
                        darkness = (int)value.Value;
                    }
                    continue;
                }

                if (cmd == "PR")
                {
                    var value = ParseInt(rest);
                    if (value.HasValue)
                    {
                        speed = (int)value.Value;
                    }
                    continue;
                }

                if (cmd == "PQ")
                {
                    var value = ParseInt(rest);
                    if (value.HasValue)
                    {
                        quantity = (int)value.Value;
                    }
                    continue;
                }

                if (cmd == "CF")
                {
                    var parts = rest.Split(',');
                    var fontRaw = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                    var fontCode = fontRaw.Length > 0 ? fontRaw[..1] : currentFont.FontCode;
                    var parsedHeight = parts.Length > 1 ? ParseInt(parts[1]) : null;
                    var heightDots = parsedHeight is > 0 ? parsedHeight.Value : currentFont.HeightDots;
                    var parsedWidth = parts.Length > 2 ? ParseInt(parts[2]) : null;
                    var defaultWidth = fontCode == "0"
                        ? heightDots
                        : LabelMath.Round(heightDots * BitmapFontWidths.GetValueOrDefault(fontCode, 1.0));

                    currentFont = new FontState(
                        fontCode,
                        heightDots,
                        parsedWidth is > 0 ? parsedWidth.Value : defaultWidth);
                    continue;
                }

                if (cmd == "FR")
                {
                    reverseNextField = true;
                    continue;
                }

                if (cmd == "FO")
                {
                    var parts = rest.Split(',');
                    var x = parts.Length > 0 ? ParseInt(parts[0]) ?? 0 : 0;
                    var y = parts.Length > 1 ? ParseInt(parts[1]) ?? 0 : 0;
                    currentFO = new FieldOrigin(x, y);
                    continue;
                }

                if (cmd == "BY")
                {
                    var parts = rest.Split(',');
                    var width = parts.Length > 0 ? ParseInt(parts[0]) : null;
                    var ratio = parts.Length > 1 ? ParseInt(parts[1]) : null;
                    var height = parts.Length > 2 ? ParseInt(parts[2]) : null;
                    currentBy = new ByState(
                        width ?? 2,
                        ratio ?? 2,
                        height ?? 10);
                    continue;
                }

                if (cmd.StartsWith("A", StringComparison.Ordinal))
                {
                    var fontCode = cmd.Length > 1 ? cmd[1..2] : "A";
                    var parts = rest.Split(',');
                    var orientation = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : "N";
                    var heightDots = parts.Length > 1 ? ParseInt(parts[1]) : null;
                    var widthDots = parts.Length > 2 ? ParseInt(parts[2]) : null;
                    var resolvedHeight = heightDots ?? 20;
                    var resolvedWidth = widthDots ?? resolvedHeight;

                    pending = new PendingText(
                        fontCode,
                        orientation,
                        resolvedHeight,
                        resolvedWidth,
                        0.8);
                    continue;
                }

                if (cmd == "BC")
                {
                    var parts = rest.Split(',');
                    var orientation = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : "N";
                    var heightDots = parts.Length > 1 ? ParseInt(parts[1]) : null;
                    var showTextRaw = parts.Length > 2 ? parts[2] : null;
                    var showText = string.IsNullOrEmpty(showTextRaw) || string.Equals(showTextRaw, "Y", StringComparison.OrdinalIgnoreCase);

                    pending = new PendingBarcode(
                        "code128",
                        orientation,
                        heightDots ?? currentBy?.Height ?? 10,
                        showText);
                    continue;
                }

                if (cmd is "B3" or "BE" or "B8" or "BU" or "B9")
                {
                    var type = cmd switch
                    {
                        "B3" => "code39",
                        "BE" => "ean13",
                        "B8" => "ean8",
                        "BU" => "upca",
                        _ => "upce"
                    };

                    var parts = rest.Split(',');
                    var orientation = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : "N";
                    var heightIndex = cmd == "B3" ? 2 : 1;
                    var showTextIndex = cmd == "B3" ? 3 : 2;
                    var heightDots = parts.Length > heightIndex ? ParseInt(parts[heightIndex]) : null;
                    var showTextRaw = parts.Length > showTextIndex ? parts[showTextIndex] : null;
                    var showText = !string.IsNullOrEmpty(showTextRaw) && string.Equals(showTextRaw, "Y", StringComparison.OrdinalIgnoreCase);

                    pending = new PendingBarcode(
                        type,
                        orientation,
                        heightDots ?? currentBy?.Height ?? 10,
                        showText);
                    continue;
                }

                if (cmd == "BQ")
                {
                    var parts = rest.Split(',');
                    var orientation = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : "N";
                    var magnification = parts.Length > 2 ? ParseInt(parts[2]) : null;
                    var errorCorrection = parts.Length > 3 ? parts[3] : null;

                    pending = new PendingQr(
                        orientation,
                        magnification ?? 4,
                        errorCorrection);
                    continue;
                }

                if (cmd == "GB")
                {
                    var parts = rest.Split(',');
                    var widthDots = parts.Length > 0 ? ParseInt(parts[0]) : null;
                    var heightDots = parts.Length > 1 ? ParseInt(parts[1]) : null;
                    var thicknessDots = parts.Length > 2 ? ParseInt(parts[2]) : null;

                    pending = new PendingGb(
                        widthDots ?? 1,
                        heightDots ?? 1,
                        thicknessDots ?? 1);
                    continue;
                }

                if (cmd == "GD")
                {
                    var parts = rest.Split(',');
                    var widthDots = parts.Length > 0 ? ParseInt(parts[0]) : null;
                    var heightDots = parts.Length > 1 ? ParseInt(parts[1]) : null;
                    var thicknessDots = parts.Length > 2 ? ParseInt(parts[2]) : null;
                    var orientation = parts.Length > 4 ? parts[4] : null;

                    pending = new PendingGd(
                        widthDots ?? 1,
                        heightDots ?? 1,
                        thicknessDots ?? 1,
                        orientation);
                    continue;
                }

                if (cmd == "FD")
                {
                    pending ??= new PendingText(
                        currentFont.FontCode,
                        "N",
                        currentFont.HeightDots,
                        currentFont.WidthDots,
                        null);

                    pendingData = DecodeHexEscapes(rest, hexIndicator);
                    continue;
                }

                if (cmd == "FH")
                {
                    hexIndicator = rest.Length > 0 ? rest[0] : '_';
                    continue;
                }

                if (cmd != "FS")
                {
                    continue;
                }

                if (currentFO is null || pending is null)
                {
                    pending = null;
                    pendingData = null;
                    reverseNextField = false;
                    continue;
                }

                switch (pending)
                {
                    case PendingText text:
                    {
                        var textContent = pendingData ?? string.Empty;
                        var averageCharacterWidth = text.FontCode == "0" ? 0.4 : 0.6;
                        var approxWidthDots = Math.Max(
                            1,
                            LabelMath.Round((textContent.Length > 0 ? textContent.Length : 1) * text.WidthDots * averageCharacterWidth));

                        UpdateMax(currentFO.X, currentFO.Y, approxWidthDots, Math.Max(1, text.HeightDots));
                        fields.Add(new ZplTextField(
                            currentFO.X,
                            currentFO.Y,
                            reverseNextField,
                            text.FontCode,
                            text.Orientation,
                            text.HeightDots,
                            text.WidthDots,
                            textContent,
                            text.AscentRatio));
                        break;
                    }

                    case PendingBarcode barcode:
                    {
                        var data = pendingData ?? string.Empty;
                        var narrowDots = Math.Max(1, currentBy?.Width ?? 2);
                        var ratio = Math.Max(1, currentBy?.Ratio ?? 2);
                        var moduleCount = GetBarcodeModuleCount(barcode.Type, data);

                        UpdateMax(currentFO.X, currentFO.Y, moduleCount * narrowDots, Math.Max(1, barcode.HeightDots));
                        fields.Add(new ZplBarcodeField(
                            currentFO.X,
                            currentFO.Y,
                            reverseNextField,
                            barcode.Type,
                            barcode.Orientation,
                            barcode.HeightDots,
                            barcode.ShowText,
                            narrowDots,
                            ratio,
                            data));
                        break;
                    }

                    case PendingQr qr:
                    {
                        var rawPayload = pendingData ?? string.Empty;
                        var payload = rawPayload.StartsWith("LA,", StringComparison.Ordinal)
                            ? rawPayload[3..]
                            : rawPayload;
                        var approxSizeDots = Math.Max(1, LabelMath.Round(25 * Math.Max(1, qr.Magnification)));

                        UpdateMax(currentFO.X, currentFO.Y, approxSizeDots, approxSizeDots);
                        fields.Add(new ZplQrField(
                            currentFO.X,
                            currentFO.Y,
                            reverseNextField,
                            qr.Orientation,
                            qr.Magnification,
                            qr.ErrorCorrection,
                            payload));
                        break;
                    }

                    case PendingGb gb:
                    {
                        UpdateMax(currentFO.X, currentFO.Y, Math.Max(1, gb.WidthDots), Math.Max(1, gb.HeightDots));
                        fields.Add(new ZplGbField(
                            currentFO.X,
                            currentFO.Y,
                            reverseNextField,
                            gb.WidthDots,
                            gb.HeightDots,
                            gb.ThicknessDots));
                        break;
                    }

                    case PendingGd gd:
                    {
                        UpdateMax(currentFO.X, currentFO.Y, Math.Max(1, gd.WidthDots), Math.Max(1, gd.HeightDots));
                        fields.Add(new ZplGdField(
                            currentFO.X,
                            currentFO.Y,
                            reverseNextField,
                            gd.WidthDots,
                            gd.HeightDots,
                            gd.ThicknessDots,
                            gd.Orientation));
                        break;
                    }
                }

                pending = null;
                pendingData = null;
                reverseNextField = false;
            }

            var widthDotsForInference = pwDots ?? Math.Max(1, maxXDots);
            var heightDotsForInference = llDots ?? Math.Max(1, maxYDots);
            var textHeights = fields
                .Where(field => field is ZplTextField)
                .Select(field => ((ZplTextField)field).HeightDots)
                .ToArray();

            var sourceDotsPerMm = InferDotsPerMm(widthDotsForInference, heightDotsForInference, textHeights);
            double DotsToMm(double dots) => dots / sourceDotsPerMm;

            var positiveXs = fields.Select(field => field.FoX).Where(value => value > 0).ToArray();
            var positiveYs = fields.Select(field => field.FoY).Where(value => value > 0).ToArray();
            var minFieldX = positiveXs.Length > 0 ? positiveXs.Min() : (double?)null;
            var minFieldY = positiveYs.Length > 0 ? positiveYs.Min() : (double?)null;
            var defaultMarginDots = LabelMath.Round(10 * sourceDotsPerMm);

            double InferMargin(double? firstFieldOffset) =>
                firstFieldOffset.HasValue && firstFieldOffset.Value <= 100
                    ? LabelMath.Round(firstFieldOffset.Value)
                    : defaultMarginDots;

            var widthMm = hasPW && pwDots.HasValue
                ? DotsToMm(pwDots.Value)
                : DotsToMm(Math.Max(1, maxXDots + InferMargin(minFieldX)));
            var heightMm = hasLL && llDots.HasValue
                ? DotsToMm(llDots.Value)
                : DotsToMm(Math.Max(1, maxYDots + InferMargin(minFieldY)));

            if (!double.IsFinite(widthMm) || widthMm <= 0)
            {
                widthMm = 100;
            }
            if (!double.IsFinite(heightMm) || heightMm <= 0)
            {
                heightMm = 150;
            }

            widthMm = RoundTo(widthMm, 0.1);
            heightMm = RoundTo(heightMm, 0.1);

            var elements = new List<LabelElement>();
            var elementIndex = 0;

            foreach (var field in fields)
            {
                var id = $"zpl-{++elementIndex}";
                var xDots = Math.Max(0, LabelMath.Round(field.FoX));
                var yDots = Math.Max(0, LabelMath.Round(field.FoY));

                switch (field)
                {
                    case ZplTextField text:
                    {
                        var fontCode = string.IsNullOrEmpty(text.FontCode) ? "0" : text.FontCode;
                        var ascentRatio = text.AscentRatio
                            ?? (fontCode == "0"
                                ? (text.HeightDots >= 100 ? 0.46 : 0.7)
                                : 0.5);
                        var baselineDots = field.FoY + LabelMath.Round(text.HeightDots * ascentRatio);

                        elements.Add(new TextElement
                        {
                            Id = id,
                            X = xDots,
                            Y = Math.Max(0, LabelMath.Round(baselineDots)),
                            Rotation = ToRotation(text.Orientation),
                            Content = text.Content,
                            FontCode = fontCode,
                            Width = Math.Max(1, LabelMath.Round(text.WidthDots)),
                            Height = Math.Max(1, LabelMath.Round(text.HeightDots)),
                            ZplFieldOriginAscentRatio = ascentRatio,
                            Reverse = field.Reverse
                        });
                        break;
                    }

                    case ZplBarcodeField barcode:
                    {
                        elements.Add(new BarcodeElement
                        {
                            Id = id,
                            X = xDots,
                            Y = yDots,
                            Rotation = ToRotation(barcode.Orientation),
                            Content = barcode.Data,
                            BarcodeType = barcode.Type,
                            Width = Math.Max(1, LabelMath.Round(barcode.NarrowDots)),
                            Height = Math.Max(1, LabelMath.Round(barcode.HeightDots)),
                            Ratio = Math.Max(1, LabelMath.Round(barcode.Ratio)),
                            ShowText = barcode.ShowText,
                            Reverse = field.Reverse
                        });
                        break;
                    }

                    case ZplQrField qr:
                    {
                        elements.Add(new QrCodeElement
                        {
                            Id = id,
                            X = xDots,
                            Y = yDots,
                            Rotation = ToRotation(qr.Orientation),
                            Content = qr.Data,
                            Size = Math.Max(1, Math.Min(20, LabelMath.Round(qr.Magnification))),
                            ErrorCorrection = NormalizeErrorCorrection(qr.ErrorCorrection),
                            Reverse = field.Reverse
                        });
                        break;
                    }

                    case ZplGbField gb:
                    {
                        var width = Math.Max(1, LabelMath.Round(gb.WidthDots));
                        var height = Math.Max(1, LabelMath.Round(gb.HeightDots));
                        var thickness = Math.Max(1, LabelMath.Round(gb.ThicknessDots));

                        var isThinVertical = gb.WidthDots <= gb.ThicknessDots && gb.HeightDots > gb.ThicknessDots;
                        var isThinHorizontal = gb.HeightDots <= gb.ThicknessDots && gb.WidthDots > gb.ThicknessDots;

                        if (isThinVertical || isThinHorizontal)
                        {
                            var isVertical = isThinVertical;
                            elements.Add(new LineElement
                            {
                                Id = id,
                                X = xDots,
                                Y = yDots,
                                X2 = xDots + (isVertical ? 0 : width),
                                Y2 = yDots + (isVertical ? height : 0),
                                Thickness = thickness,
                                Reverse = field.Reverse
                            });
                            break;
                        }

                        elements.Add(new RectangleElement
                        {
                            Id = id,
                            X = xDots,
                            Y = yDots,
                            Width = width,
                            Height = height,
                            Thickness = thickness,
                            Filled = gb.ThicknessDots >= Math.Min(gb.WidthDots, gb.HeightDots),
                            Reverse = field.Reverse
                        });
                        break;
                    }

                    case ZplGdField gd:
                    {
                        var width = Math.Max(1, LabelMath.Round(gd.WidthDots));
                        var height = Math.Max(1, LabelMath.Round(gd.HeightDots));
                        var orientation = string.IsNullOrEmpty(gd.Orientation) ? "R" : gd.Orientation;

                        elements.Add(new LineElement
                        {
                            Id = id,
                            X = xDots,
                            Y = yDots,
                            X2 = xDots + width,
                            Y2 = yDots + (string.Equals(orientation, "L", StringComparison.OrdinalIgnoreCase) ? 0 : height),
                            Thickness = Math.Max(1, LabelMath.Round(gd.ThicknessDots)),
                            Reverse = field.Reverse
                        });
                        break;
                    }
                }
            }

            return new LabelDocument
            {
                Name = "Imported ZPL",
                WidthMm = widthMm,
                HeightMm = heightMm,
                Dpi = LabelMath.Round(sourceDotsPerMm * 25.4),
                Protocol = LabelProtocol.Zpl,
                Quantity = Math.Max(1, quantity),
                Speed = speed,
                Darkness = darkness,
                Elements = elements
            };
        }

        private static double InferDotsPerMm(double widthDots, double heightDots, IReadOnlyList<double> textHeightsDots)
        {
            double[] candidateDpis = { 203, 300, 600 };
            var best = 203 / 25.4;
            var bestScore = double.NegativeInfinity;

            double? Median(IReadOnlyList<double> values)
            {
                if (values.Count == 0)
                {
                    return null;
                }

                var sorted = values.OrderBy(value => value).ToArray();
                return sorted[(int)Math.Floor(sorted.Length / 2.0)];
            }

            var typicalTextHeightDots = Median(textHeightsDots);

            foreach (var dpi in candidateDpis)
            {
                var candidate = dpi / 25.4;
                var widthMm = widthDots / candidate;
                var heightMm = heightDots / candidate;
                var score = 0.0;

                if (widthMm >= 20 && widthMm <= 160)
                {
                    score += 3;
                }
                else if (widthMm >= 160 && widthMm <= 260)
                {
                    score += 1;
                }
                else
                {
                    score -= 3;
                }

                if (heightMm >= 20 && heightMm <= 260)
                {
                    score += 2;
                }
                else if (heightMm >= 260 && heightMm <= 400)
                {
                    score += 0;
                }
                else
                {
                    score -= 2;
                }

                if (widthMm > 600 || heightMm > 600)
                {
                    score -= 10;
                }

                if (typicalTextHeightDots is not null)
                {
                    var textPt = (typicalTextHeightDots.Value / candidate) * 72 / 25.4;
                    if (textPt >= 4 && textPt <= 72)
                    {
                        score += 1;
                    }
                    else if (textPt > 150)
                    {
                        score -= 2;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static double GetBarcodeModuleCount(string type, string data)
        {
            var length = data.Length > 0 ? data.Length : 1;

            return type switch
            {
                "code39" => length * 13 + 35,
                "ean13" => 95,
                "ean8" => 67,
                "upca" => 95,
                "upce" => 51,
                _ => length * 11 + 35
            };
        }

        private static int ToRotation(string? orientation)
        {
            return orientation?.ToUpperInvariant() switch
            {
                "R" => 1,
                "I" => 2,
                "B" => 3,
                _ => 0
            };
        }

        private static string NormalizeErrorCorrection(string? value)
        {
            var upper = value?.ToUpperInvariant();
            return upper is "L" or "M" or "Q" or "H" ? upper : "H";
        }

        private static double RoundTo(double value, double step)
        {
            if (!double.IsFinite(value) || step <= 0)
            {
                return value;
            }

            return LabelMath.Round(value / step) * step;
        }

        private static string DecodeHexEscapes(string value, char? indicator)
        {
            if (!indicator.HasValue || value.IndexOf(indicator.Value) < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);

            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];

                if (current == indicator.Value
                    && index + 2 < value.Length
                    && IsHexDigit(value[index + 1])
                    && IsHexDigit(value[index + 2]))
                {
                    var code = Convert.ToByte(value.Substring(index + 1, 2), 16);
                    builder.Append(Cp850.GetString(new[] { code }));
                    index += 2;
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private static bool IsHexDigit(char value) =>
            value is >= '0' and <= '9'
            or >= 'A' and <= 'F'
            or >= 'a' and <= 'f';

        private static double? ParseInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                return (int)Math.Truncate(parsedDouble);
            }

            return null;
        }

        private sealed record FieldOrigin(double X, double Y);

        private sealed record ByState(double Width, double Ratio, double Height);

        private sealed record FontState(string FontCode, double HeightDots, double WidthDots);

        private abstract record PendingField;

        private sealed record PendingText(
            string FontCode,
            string Orientation,
            double HeightDots,
            double WidthDots,
            double? AscentRatio) : PendingField;

        private sealed record PendingBarcode(
            string Type,
            string Orientation,
            double HeightDots,
            bool ShowText) : PendingField;

        private sealed record PendingQr(
            string Orientation,
            double Magnification,
            string? ErrorCorrection) : PendingField;

        private sealed record PendingGb(
            double WidthDots,
            double HeightDots,
            double ThicknessDots) : PendingField;

        private sealed record PendingGd(
            double WidthDots,
            double HeightDots,
            double ThicknessDots,
            string? Orientation) : PendingField;

        private abstract record ZplField(double FoX, double FoY, bool Reverse);

        private sealed record ZplTextField(
            double FoX,
            double FoY,
            bool Reverse,
            string FontCode,
            string Orientation,
            double HeightDots,
            double WidthDots,
            string Content,
            double? AscentRatio) : ZplField(FoX, FoY, Reverse);

        private sealed record ZplBarcodeField(
            double FoX,
            double FoY,
            bool Reverse,
            string Type,
            string Orientation,
            double HeightDots,
            bool ShowText,
            double NarrowDots,
            double Ratio,
            string Data) : ZplField(FoX, FoY, Reverse);

        private sealed record ZplQrField(
            double FoX,
            double FoY,
            bool Reverse,
            string Orientation,
            double Magnification,
            string? ErrorCorrection,
            string Data) : ZplField(FoX, FoY, Reverse);

        private sealed record ZplGbField(
            double FoX,
            double FoY,
            bool Reverse,
            double WidthDots,
            double HeightDots,
            double ThicknessDots) : ZplField(FoX, FoY, Reverse);

        private sealed record ZplGdField(
            double FoX,
            double FoY,
            bool Reverse,
            double WidthDots,
            double HeightDots,
            double ThicknessDots,
            string? Orientation) : ZplField(FoX, FoY, Reverse);
    }
}
