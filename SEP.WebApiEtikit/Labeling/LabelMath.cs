using System.Globalization;

namespace SEP.WebApiEtikit.Labeling
{
    internal static class LabelMath
    {
        /// <summary>
        /// JavaScript's Math.round equivalent (round half away from zero).
        /// </summary>
        public static double Round(double value) =>
            Math.Round(value, MidpointRounding.AwayFromZero);

        public static string Format(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
