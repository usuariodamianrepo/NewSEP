using System.Globalization;
using System.Text.RegularExpressions;

namespace SEP.WebApiEtikit.Labeling
{
    /// <summary>
    /// Port of etikit's TPCLDriver.parse (src/drivers/tpcl.ts).
    /// Supports the TPCL subset used by the project: {D}, {LC}, {XR},
    /// {PC}/{RC}, {PV}/{RV}, {XB}/{RB} and {XS}.
    /// </summary>
    public sealed class TpclLabelParser
    {
        private static readonly Regex CommandRegex = new(@"\{[^}]+\|?\}", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> ReverseBarcodeMap = new()
        {
            ["9"] = "code128",
            ["2"] = "ean13",
            ["1"] = "ean8",
            ["3"] = "upca",
            ["4"] = "upce",
            ["5"] = "code39"
        };

        private static readonly double[] PreferredBaseDots = { 2, 3, 5, 6, 7, 10, 12, 15, 20 };

        public LabelDocument Parse(string tpcl)
        {
            if (string.IsNullOrWhiteSpace(tpcl))
            {
                throw new LabelParseException("El código TPCL está vacío.");
            }

            var elements = new List<LabelElement>();
            double widthMm = 100;
            double heightMm = 150;
            var quantity = 1;

            var textDefs = new Dictionary<string, TextDefinition>(StringComparer.Ordinal);
            var barcodeDefs = new Dictionary<string, BarcodeDefinition>(StringComparer.Ordinal);
            var dotLikeValues = new List<double>();
            var elementIndex = 0;

            string NextId() => $"tpcl-{++elementIndex}";

            void EmitBarcode(BarcodeDefinition definition, string content)
            {
                var parameters = definition.Parameters;

                if (parameters.Length >= 7 && string.Equals(parameters[2], "T", StringComparison.Ordinal))
                {
                    elements.Add(new QrCodeElement
                    {
                        Id = definition.Id,
                        X = ParseIntOrZero(parameters[0]),
                        Y = ParseIntOrZero(parameters[1]),
                        Rotation = ParseIntOrZero(parameters[6]),
                        Size = ParseIntOrZero(parameters[4]),
                        Content = content,
                        ErrorCorrection = NormalizeErrorCorrection(parameters[3]),
                        QrModel = parameters.Length > 7
                            ? parameters[7] switch
                            {
                                "M2" => "M2",
                                "M3" when string.Equals(parameters[3], "L", StringComparison.Ordinal) => "M3",
                                _ => "M1"
                            }
                            : "M1"
                    });
                }
                else
                {
                    elements.Add(new BarcodeElement
                    {
                        Id = definition.Id,
                        X = ParseIntOrZero(parameters[0]),
                        Y = ParseIntOrZero(parameters[1]),
                        Rotation = parameters.Length > 5 ? ParseIntOrZero(parameters[5]) : 0,
                        BarcodeType = parameters.Length > 2 && ReverseBarcodeMap.TryGetValue(parameters[2], out var type)
                            ? type
                            : "code128",
                        Width = parameters.Length > 4 ? ParseIntOrZero(parameters[4]) : 0,
                        Height = parameters.Length > 6 ? ParseIntOrZero(parameters[6]) : 0,
                        Content = content
                    });
                }

                definition.Emitted = true;
            }

            foreach (Match match in CommandRegex.Matches(tpcl))
            {
                var cleanCommand = CleanCommand(match.Value);
                var parts = cleanCommand.Split(';');
                var commandType = parts[0];

                if (cleanCommand.StartsWith("D", StringComparison.Ordinal) && parts.Length == 1)
                {
                    var parameters = cleanCommand[1..].Split(',');
                    if (parameters.Length >= 2)
                    {
                        heightMm = ParseIntOrZero(parameters[0]) / 10.0;
                        widthMm = ParseIntOrZero(parameters[1]) / 10.0;
                    }
                    continue;
                }

                if (commandType == "D" && parts.Length > 1)
                {
                    var parameters = parts[1].Split(',');
                    if (parameters.Length >= 2)
                    {
                        heightMm = ParseIntOrZero(parameters[0]) / 10.0;
                        widthMm = ParseIntOrZero(parameters[1]) / 10.0;
                    }
                    continue;
                }

                if (commandType == "LC")
                {
                    var parameters = ParameterList(parts);
                    if (parameters.Length >= 6)
                    {
                        var thickness = ParseIntOrZero(parameters[5]);
                        if (thickness > 0)
                        {
                            dotLikeValues.Add(thickness);
                        }

                        elements.Add(new LineElement
                        {
                            Id = NextId(),
                            X = ParseIntOrZero(parameters[0]),
                            Y = ParseIntOrZero(parameters[1]),
                            X2 = ParseIntOrZero(parameters[2]),
                            Y2 = ParseIntOrZero(parameters[3]),
                            Thickness = thickness
                        });
                    }
                    continue;
                }

                if (commandType == "XR")
                {
                    var parameters = ParameterList(parts);
                    if (parameters.Length >= 5)
                    {
                        var x1 = ParseIntOrZero(parameters[0]);
                        var y1 = ParseIntOrZero(parameters[1]);
                        var x2 = ParseIntOrZero(parameters[2]);
                        var y2 = ParseIntOrZero(parameters[3]);

                        elements.Add(new RectangleElement
                        {
                            Id = NextId(),
                            X = x1,
                            Y = y1,
                            Width = x2 - x1,
                            Height = y2 - y1,
                            Thickness = 3
                        });
                    }
                    continue;
                }

                if (commandType.StartsWith("PC", StringComparison.Ordinal))
                {
                    var parameters = ParameterList(parts);
                    if (parameters.Length >= 6)
                    {
                        textDefs[commandType] = new TextDefinition
                        {
                            X = ParseIntOrZero(parameters[0]),
                            Y = ParseIntOrZero(parameters[1]),
                            Height = ParseIntOrZero(parameters[2]),
                            Width = ParseIntOrZero(parameters[3]),
                            Font = parameters[4],
                            Rotation = ParseIntOrZero(parameters[5]),
                            Id = NextId()
                        };
                    }
                    continue;
                }

                if (commandType.StartsWith("RC", StringComparison.Ordinal))
                {
                    var pcType = ReplacePrefix(commandType, "RC", "PC");
                    if (textDefs.TryGetValue(pcType, out var definition))
                    {
                        elements.Add(new TextElement
                        {
                            Id = definition.Id,
                            X = definition.X,
                            Y = definition.Y,
                            Rotation = definition.Rotation,
                            FontCode = definition.Font,
                            Width = definition.Width,
                            Height = definition.Height,
                            Content = NormalizeEncoding(ContentAfterSemicolon(cleanCommand, commandType))
                        });
                    }
                    continue;
                }

                if (commandType.StartsWith("PV", StringComparison.Ordinal))
                {
                    var commandData = ContentAfterSemicolon(cleanCommand, commandType);
                    var parameters = commandData.Split(',');

                    if (parameters.Length >= 7)
                    {
                        var attributeAndData = string.Join(",", parameters.Skip(6));
                        var inlineSeparator = attributeAndData.IndexOf('=');
                        var font = parameters[4];

                        var definition = new TextDefinition
                        {
                            X = ParseIntOrZero(parameters[0]),
                            Y = ParseIntOrZero(parameters[1]),
                            Height = ParseIntOrZero(parameters[2]),
                            Width = ParseIntOrZero(parameters[3]),
                            Font = font,
                            Rotation = ParseIntOrZero(parameters[5]),
                            Id = NextId(),
                            InlineText = inlineSeparator >= 0,
                            Content = inlineSeparator >= 0
                                ? CleanText(attributeAndData[(inlineSeparator + 1)..])
                                : null
                        };

                        textDefs[commandType] = definition;

                        if (definition.InlineText)
                        {
                            elements.Add(new TextElement
                            {
                                Id = definition.Id,
                                X = definition.X,
                                Y = definition.Y,
                                Rotation = definition.Rotation,
                                FontCode = definition.Font,
                                Width = OutlineScaleFromSize(definition.Width, definition.Font),
                                Height = OutlineScaleFromSize(definition.Height, definition.Font),
                                FontRendering = FontRendering.Outline,
                                InlineText = true,
                                Content = definition.Content
                            });
                        }
                    }
                    continue;
                }

                if (commandType.StartsWith("RV", StringComparison.Ordinal))
                {
                    var pvType = ReplacePrefix(commandType, "RV", "PV");
                    if (textDefs.TryGetValue(pvType, out var definition))
                    {
                        elements.Add(new TextElement
                        {
                            Id = definition.Id,
                            X = definition.X,
                            Y = definition.Y,
                            Rotation = definition.Rotation,
                            FontCode = definition.Font,
                            Width = OutlineScaleFromSize(definition.Width, definition.Font),
                            Height = OutlineScaleFromSize(definition.Height, definition.Font),
                            FontRendering = FontRendering.Outline,
                            InlineText = false,
                            Content = CleanText(ContentAfterSemicolon(cleanCommand, commandType))
                        });
                    }
                    continue;
                }

                if (commandType.StartsWith("XB", StringComparison.Ordinal))
                {
                    var parameters = ParameterList(parts);
                    if (parameters.Length >= 5)
                    {
                        var value = ParseIntOrZero(parameters[4]);
                        if (value > 0)
                        {
                            dotLikeValues.Add(value);
                        }
                    }

                    var definition = new BarcodeDefinition
                    {
                        Parameters = parameters,
                        Id = NextId()
                    };

                    var inlineContent = ExtractInlineData(parameters);
                    if (!string.IsNullOrEmpty(inlineContent))
                    {
                        EmitBarcode(definition, inlineContent);
                    }

                    barcodeDefs[commandType] = definition;
                    continue;
                }

                if (commandType.StartsWith("RB", StringComparison.Ordinal))
                {
                    var xbType = ReplacePrefix(commandType, "RB", "XB");
                    if (!barcodeDefs.TryGetValue(xbType, out var definition) || definition.Emitted)
                    {
                        continue;
                    }

                    EmitBarcode(definition, ContentAfterSemicolon(cleanCommand, commandType));
                    continue;
                }

                if (commandType == "XS")
                {
                    var rest = parts.Length > 1 ? parts[1] : string.Empty;
                    var parameters = rest.Split(',');
                    if (parameters.Length > 1 && int.TryParse(parameters[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQuantity))
                    {
                        quantity = Math.Max(1, parsedQuantity);
                    }
                }
            }

            var sourceDpi = InferDpi(dotLikeValues);
            var sourceDotsPerMm = sourceDpi / 25.4;

            double TargetDotsToBaseDots(double dots) =>
                Math.Max(1, Math.Min(99, LabelMath.Round(dots * 8 / sourceDotsPerMm)));

            foreach (var element in elements)
            {
                switch (element)
                {
                    case BarcodeElement barcode:
                        barcode.Width = TargetDotsToBaseDots(barcode.Width);
                        break;
                    case QrCodeElement qr:
                        qr.Size = TargetDotsToBaseDots(qr.Size);
                        break;
                    case LineElement line:
                        line.Thickness = TargetDotsToBaseDots(line.Thickness);
                        break;
                }
            }

            if (!double.IsFinite(widthMm) || widthMm <= 0)
            {
                widthMm = 100;
            }
            if (!double.IsFinite(heightMm) || heightMm <= 0)
            {
                heightMm = 150;
            }

            return new LabelDocument
            {
                Name = "Imported TPCL",
                WidthMm = widthMm,
                HeightMm = heightMm,
                Dpi = sourceDpi,
                Protocol = LabelProtocol.Tpcl,
                Quantity = quantity,
                Elements = elements
            };
        }

        private static string CleanCommand(string command)
        {
            var clean = command.StartsWith("{", StringComparison.Ordinal) ? command[1..] : command;

            if (clean.EndsWith("|}", StringComparison.Ordinal))
            {
                return clean[..^2];
            }

            if (clean.EndsWith("}", StringComparison.Ordinal))
            {
                return clean[..^1];
            }

            return clean;
        }

        private static string[] ParameterList(string[] parts) =>
            parts.Length > 1 ? parts[1].Split(',') : Array.Empty<string>();

        private static string ContentAfterSemicolon(string cleanCommand, string commandType)
        {
            var index = commandType.Length + 1;
            return cleanCommand.Length > index ? cleanCommand[index..] : string.Empty;
        }

        private static string ReplacePrefix(string value, string prefix, string replacement) =>
            value.StartsWith(prefix, StringComparison.Ordinal)
                ? replacement + value[prefix.Length..]
                : value;

        private static double OutlineScaleFromSize(double size, string font)
        {
            var fontSizePt = LabelFontCatalog.ResolveFontSizePt(font);
            var nominalSize = fontSizePt * 25.4 / 72 * 10;
            var scale = Math.Max(0.1, LabelMath.Round((size / nominalSize) * 100) / 100);
            return scale >= 10 ? LabelMath.Round(scale * 10) : scale;
        }

        private static double InferDpi(IReadOnlyList<double> values)
        {
            double[] candidates = { 203, 300, 600 };
            if (values.Count == 0)
            {
                return 203;
            }

            var best = 203.0;
            var bestScore = double.NegativeInfinity;

            foreach (var dpi in candidates)
            {
                var targetDotsPerMm = dpi / 25.4;
                var score = 0.0;

                foreach (var value in values)
                {
                    if (!double.IsFinite(value) || value <= 0)
                    {
                        continue;
                    }

                    var baseDots = value * 8 / targetDotsPerMm;
                    if (!double.IsFinite(baseDots) || baseDots < 0.5 || baseDots > 120)
                    {
                        score -= 3;
                        continue;
                    }

                    var nearest = LabelMath.Round(baseDots);
                    score -= Math.Abs(baseDots - nearest);

                    if (nearest < 1 || nearest > 99)
                    {
                        score -= 2;
                        continue;
                    }

                    var bestDistance = PreferredBaseDots
                        .Select(preferred => Math.Abs(nearest - preferred))
                        .Min();

                    score += 2 - bestDistance * 0.8;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = dpi;
                }
            }

            return best;
        }

        private static string NormalizeErrorCorrection(string? value)
        {
            var upper = value?.ToUpperInvariant();
            return upper is "L" or "M" or "Q" or "H" ? upper : "H";
        }

        private static string? ExtractInlineData(string[] parameters)
        {
            foreach (var parameter in parameters)
            {
                var separator = parameter.IndexOf('=');
                if (separator >= 0)
                {
                    return CleanText(parameter[(separator + 1)..]);
                }
            }

            return null;
        }

        private static string CleanText(string value)
        {
            var trimmed = value.Trim();
            var text = trimmed.Length >= 2 && trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)
                ? trimmed[1..^1]
                : trimmed;

            return NormalizeEncoding(text);
        }

        /// <summary>
        /// Some sample TPCL payloads store CP850 bytes as their Latin-1 equivalents
        /// (0xA2 = ó). Repair the only character used by the sample data.
        /// </summary>
        private static string NormalizeEncoding(string value) =>
            value.Replace('\u00A2', '\u00F3');

        private static int ParseIntOrZero(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            var trimmed = value.Trim();
            return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private sealed class TextDefinition
        {
            public int X { get; init; }

            public int Y { get; init; }

            public int Height { get; init; }

            public int Width { get; init; }

            public string Font { get; init; } = "A";

            public int Rotation { get; init; }

            public string Id { get; init; } = string.Empty;

            public bool InlineText { get; init; }

            public string? Content { get; init; }
        }

        private sealed class BarcodeDefinition
        {
            public string[] Parameters { get; init; } = Array.Empty<string>();

            public string Id { get; init; } = string.Empty;

            public bool Emitted { get; set; }
        }
    }
}
