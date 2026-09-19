using System;
using System.Globalization;

namespace Bfocus.Widget
{
    /// <summary>Mapeia idiomas para os três do widget: pt* → pt_BR, es* → es, resto → en.</summary>
    public static class LocaleResolver
    {
        public static string Resolve(string? configured) =>
            string.IsNullOrWhiteSpace(configured) ? FromCulture(CultureInfo.CurrentUICulture) : Normalize(configured!);

        public static string Normalize(string locale)
        {
            var l = locale.Trim();
            if (l.Length == 0) return Protocol.DefaultLocale;
            if (l.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt_BR";
            if (l.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es";
            return "en";
        }

        public static string FromCulture(CultureInfo? culture)
        {
            // Cultura invariante (servidores, contêineres) cai no padrão do protocolo.
            if (culture == null || string.IsNullOrEmpty(culture.Name)) return Protocol.DefaultLocale;
            return Normalize(culture.Name);
        }
    }
}
