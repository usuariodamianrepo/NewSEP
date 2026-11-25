namespace SEP.WebApiEtikit.Labeling
{
    public sealed record LabelFontMetadata(
        string Key,
        string Label,
        string FontFamily,
        double FontSizePt,
        string FontWeight,
        string FontStyle);

    public static class LabelFontCatalog
    {
        public static readonly IReadOnlyList<LabelFontMetadata> ZplFonts = new[]
        {
            new LabelFontMetadata("0", "Scalable Triumvirate Bold Condensed", "helvetica", 10, "bold", "normal"),
            new LabelFontMetadata("A", "Standard", "courier", 5, "normal", "normal"),
            new LabelFontMetadata("B", "Bold", "courier", 7, "bold", "normal"),
            new LabelFontMetadata("C", "OCR-B", "ocr-b", 12, "normal", "normal"),
            new LabelFontMetadata("D", "OCR-A", "ocr-a", 10, "normal", "normal"),
            new LabelFontMetadata("E", "Sans Serif Medium", "helvetica", 12, "normal", "normal"),
            new LabelFontMetadata("F", "Sans Serif Bold", "helvetica", 14, "bold", "normal"),
            new LabelFontMetadata("G", "Sans Serif Extra Bold", "helvetica", 18, "bold", "normal"),
            new LabelFontMetadata("H", "Sans Serif Ultra Bold", "helvetica", 12, "bold", "normal"),
            new LabelFontMetadata("P", "Proportional", "times-roman", 10, "normal", "normal"),
            new LabelFontMetadata("Q", "Proportional Bold", "times-roman", 10, "bold", "normal"),
            new LabelFontMetadata("R", "Serif", "times-roman", 10, "normal", "normal"),
            new LabelFontMetadata("S", "Serif Bold", "times-roman", 10, "bold", "normal"),
            new LabelFontMetadata("T", "Typewriter / Monospace", "courier", 10, "normal", "normal"),
            new LabelFontMetadata("U", "Ultra-thin", "helvetica", 10, "normal", "normal"),
            new LabelFontMetadata("V", "Very Large", "helvetica", 24, "normal", "normal")
        };

        public static readonly IReadOnlyList<LabelFontMetadata> TpclFonts = new[]
        {
            new LabelFontMetadata("A", "Times Roman 8pt Medium", "times", 8, "normal", "normal"),
            new LabelFontMetadata("B", "Times Roman 10pt Medium", "times", 10, "normal", "normal"),
            new LabelFontMetadata("C", "Times Roman 10pt Bold", "times", 10, "bold", "normal"),
            new LabelFontMetadata("D", "Times Roman 12pt Bold", "times", 12, "bold", "normal"),
            new LabelFontMetadata("E", "Times Roman 14pt Bold", "times", 14, "bold", "normal"),
            new LabelFontMetadata("F", "Times Roman 12pt Italic", "times", 12, "normal", "italic"),
            new LabelFontMetadata("G", "Helvetica 6pt Medium", "helvetica", 6, "normal", "normal"),
            new LabelFontMetadata("H", "Helvetica 10pt Medium", "helvetica", 10, "normal", "normal"),
            new LabelFontMetadata("I", "Helvetica 12pt Medium", "helvetica", 12, "normal", "normal"),
            new LabelFontMetadata("J", "Helvetica 12pt Bold", "helvetica", 12, "bold", "normal"),
            new LabelFontMetadata("K", "Helvetica 14pt Bold", "helvetica", 14, "bold", "normal"),
            new LabelFontMetadata("L", "Helvetica 12pt Italic", "helvetica", 12, "normal", "italic"),
            new LabelFontMetadata("M", "Presentation Bold 18pt", "presentation", 18, "bold", "normal"),
            new LabelFontMetadata("N", "Letter Gothic 9.5pt", "letter-gothic", 9.5, "normal", "normal"),
            new LabelFontMetadata("O", "Prestige Elite 7pt", "prestige-elite", 7, "normal", "normal"),
            new LabelFontMetadata("P", "Prestige Elite 10pt", "prestige-elite", 10, "normal", "normal"),
            new LabelFontMetadata("Q", "Courier 10pt", "courier", 10, "normal", "normal"),
            new LabelFontMetadata("R", "Courier Bold 12pt", "courier", 12, "bold", "normal"),
            new LabelFontMetadata("S", "OCR-A 12pt", "ocr-a", 12, "normal", "normal"),
            new LabelFontMetadata("T", "OCR-B 12pt", "ocr-b", 12, "normal", "normal")
        };

        private static readonly Dictionary<string, string> CssFontFamilies = new(StringComparer.OrdinalIgnoreCase)
        {
            ["times"] = "\"Times New Roman\", Times, serif",
            ["times-roman"] = "\"Times New Roman\", Times, serif",
            ["helvetica"] = "Helvetica, Arial, sans-serif",
            ["courier"] = "\"Courier New\", Courier, monospace",
            ["presentation"] = "\"Arial Black\", Arial, sans-serif",
            ["letter-gothic"] = "\"Courier New\", Courier, monospace",
            ["prestige-elite"] = "\"Courier New\", Courier, monospace",
            ["ocr-a"] = "\"OCR A Std\", \"OCR A\", monospace",
            ["ocr-b"] = "\"OCR B Std\", \"OCR B\", monospace"
        };

        public static LabelFontMetadata Resolve(string? fontCode, LabelProtocol protocol)
        {
            var fonts = protocol == LabelProtocol.Zpl ? ZplFonts : TpclFonts;
            return fonts.FirstOrDefault(font => string.Equals(font.Key, fontCode, StringComparison.Ordinal))
                   ?? fonts[0];
        }

        public static string GetCssFontFamily(string fontFamily) =>
            CssFontFamilies.TryGetValue(fontFamily, out var css) ? css : CssFontFamilies["helvetica"];

        public static double ResolveFontSizePt(string fontCode)
        {
            var font = TpclFonts.FirstOrDefault(item => string.Equals(item.Key, fontCode, StringComparison.Ordinal));
            return font?.FontSizePt ?? TpclFonts[0].FontSizePt;
        }
    }
}
