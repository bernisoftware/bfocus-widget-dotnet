using System.IO;
using System.Linq;
using System.Text;

namespace Bfocus.Widget
{
    /// <summary>Nome de arquivo seguro para "salvar como" (o nome vem do embed, não do usuário).</summary>
    public static class FileNames
    {
        private static readonly char[] Invalid =
            Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).Distinct().ToArray();

        public static string Sanitize(string? name, string fallback = "download")
        {
            var n = (name ?? string.Empty).Trim();
            // Só o último segmento: "../../x.exe" não escapa da pasta escolhida.
            var slash = n.LastIndexOfAny(new[] { '/', '\\' });
            if (slash >= 0) n = n.Substring(slash + 1);
            var sb = new StringBuilder(n.Length);
            foreach (var c in n) sb.Append(c < 0x20 || Invalid.Contains(c) ? '_' : c);
            n = sb.ToString().Trim(' ', '.');
            if (n.Length > 180) n = n.Substring(0, 180);
            return n.Length == 0 ? fallback : n;
        }
    }
}
