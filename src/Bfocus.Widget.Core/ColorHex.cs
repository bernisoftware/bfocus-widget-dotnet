using System.Globalization;

namespace Bfocus.Widget
{
    /// <summary>Leitura de cor <c>#RGB</c>/<c>#RRGGBB</c> vinda do tenant, comum aos toolkits.</summary>
    public static class ColorHex
    {
        public static bool TryParse(string? value, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var s = value!.Trim();
            if (s.StartsWith("#", System.StringComparison.Ordinal)) s = s.Substring(1);
            if (s.Length == 3) s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            if (s.Length != 6) return false;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
            r = (byte)((rgb >> 16) & 0xFF);
            g = (byte)((rgb >> 8) & 0xFF);
            b = (byte)(rgb & 0xFF);
            return true;
        }

        /// <summary>Cor válida ou a da marca bFocus.</summary>
        public static void ParseOrFallback(string? value, out byte r, out byte g, out byte b)
        {
            if (!TryParse(value, out r, out g, out b)) TryParse(Protocol.BrandFallbackColor, out r, out g, out b);
        }
    }
}
