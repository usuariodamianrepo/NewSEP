using System.Text;
using ZXing;
using ZXing.QrCode.Internal;

namespace SEP.WebApiEtikit.Labeling
{
    public sealed record BarcodeMatrix(int Width, int Height, string PathData);

    /// <summary>
    /// Encodes barcodes/QR codes with ZXing.Net and exposes their modules as a single SVG path.
    /// </summary>
    public static class BarcodeSvgFactory
    {
        public static BarcodeMatrix? TryEncode(string? content, string barcodeType, string? errorCorrection = null)
        {
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            var format = MapFormat(barcodeType);
            if (format is null)
            {
                return null;
            }

            try
            {
                var hints = new Dictionary<EncodeHintType, object>
                {
                    [EncodeHintType.MARGIN] = 0
                };

                if (format == BarcodeFormat.QR_CODE)
                {
                    var level = MapErrorCorrection(errorCorrection);
                    if (level is not null)
                    {
                        hints[EncodeHintType.ERROR_CORRECTION] = level;
                    }
                }

                var matrix = new MultiFormatWriter().encode(content, format.Value, 0, 0, hints);
                return new BarcodeMatrix(matrix.Width, matrix.Height, BuildPath(matrix));
            }
            catch
            {
                // Invalid data for the requested symbology: the element is skipped.
                return null;
            }
        }

        private static BarcodeFormat? MapFormat(string barcodeType) =>
            barcodeType.ToLowerInvariant() switch
            {
                "code128" => BarcodeFormat.CODE_128,
                "code39" => BarcodeFormat.CODE_39,
                "ean13" => BarcodeFormat.EAN_13,
                "ean8" => BarcodeFormat.EAN_8,
                "upca" => BarcodeFormat.UPC_A,
                "upce" => BarcodeFormat.UPC_E,
                "qrcode" or "qr" => BarcodeFormat.QR_CODE,
                _ => null
            };

        private static ErrorCorrectionLevel? MapErrorCorrection(string? value) =>
            value?.ToUpperInvariant() switch
            {
                "L" => ErrorCorrectionLevel.L,
                "M" => ErrorCorrectionLevel.M,
                "Q" => ErrorCorrectionLevel.Q,
                "H" => ErrorCorrectionLevel.H,
                _ => ErrorCorrectionLevel.H
            };

        private static string BuildPath(ZXing.Common.BitMatrix matrix)
        {
            var builder = new StringBuilder();

            for (var y = 0; y < matrix.Height; y++)
            {
                var x = 0;
                while (x < matrix.Width)
                {
                    if (!matrix[x, y])
                    {
                        x++;
                        continue;
                    }

                    var start = x;
                    while (x < matrix.Width && matrix[x, y])
                    {
                        x++;
                    }

                    var length = x - start;
                    builder.Append('M').Append(start).Append(' ').Append(y)
                        .Append('h').Append(length)
                        .Append("v1h-").Append(length).Append('z');
                }
            }

            return builder.ToString();
        }
    }
}
