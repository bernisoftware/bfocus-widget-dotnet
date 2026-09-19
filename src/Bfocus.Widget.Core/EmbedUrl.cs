using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bfocus.Widget
{
    /// <summary>Página servida pela CDN.</summary>
    public enum EmbedPage
    {
        /// <summary><c>embed.html</c>: chamados e chat.</summary>
        Tickets,
        /// <summary><c>release-notes.html</c>: banner e histórico.</summary>
        ReleaseNotes,
    }

    /// <summary>Quem hospeda o embed.</summary>
    public enum HostMode
    {
        /// <summary><c>host=native</c>: dentro da WebView do pacote.</summary>
        Native,
        /// <summary><c>host=browser</c>: página inteira no navegador do sistema (sem WebView).</summary>
        Browser,
    }

    /// <summary>URL do embed (§1 do protocolo). Os parâmetros vão no fragmento para a identidade não ir aos logs da CDN.</summary>
    public static class EmbedUrl
    {
        public static string Build(
            WidgetIdentity identity,
            EmbedPage page = EmbedPage.Tickets,
            HostMode mode = HostMode.Native,
            string? open = null,
            string? view = null,
            IEnumerable<string>? ids = null,
            IEnumerable<KeyValuePair<string, string>>? extra = null)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            var p = new List<KeyValuePair<string, string>>
            {
                Kv("host", mode == HostMode.Browser ? "browser" : "native"),
                Kv("hp", Protocol.HostProtocol.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Kv("client", identity.Client),
                Kv("key", identity.PublishableKey),
                Kv("user", identity.UserBase64),
                Kv("locale", identity.Locale),
                Kv("parentOrigin", identity.ParentOrigin),
            };
            if (identity.Product != null) p.Add(Kv("product", identity.Product));
            if (identity.Audience != null) p.Add(Kv("audience", identity.Audience));
            // `api` só quando difere do padrão (homologação/testes).
            if (!string.Equals(identity.ApiBaseUrl, Protocol.DefaultApiBaseUrl, StringComparison.Ordinal))
                p.Add(Kv("api", identity.ApiBaseUrl));
            if (page == EmbedPage.Tickets && !identity.ShowReleaseNotes) p.Add(Kv("showReleaseNotes", "0"));
            if (!string.IsNullOrEmpty(open)) p.Add(Kv("open", open!));
            if (!string.IsNullOrEmpty(view)) p.Add(Kv("view", view!));
            var idList = ids?.Where(i => !string.IsNullOrEmpty(i)).ToList();
            if (idList != null && idList.Count > 0) p.Add(Kv("ids", string.Join(",", idList)));
            if (extra != null) p.AddRange(extra);

            var sb = new StringBuilder(identity.EmbedBaseUrl);
            sb.Append('/').Append(page == EmbedPage.Tickets ? "embed.html" : "release-notes.html").Append('#');
            for (var i = 0; i < p.Count; i++)
            {
                if (i > 0) sb.Append('&');
                sb.Append(p[i].Key).Append('=').Append(Rfc3986.Encode(p[i].Value));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Mesmo documento (só o fragmento muda)? Navegar assim não recarrega a página, e o embed só
        /// lê os parâmetros ao carregar.
        /// </summary>
        public static bool IsSameDocument(string? current, string next)
        {
            if (string.IsNullOrEmpty(current)) return false;
            return string.Equals(StripFragment(current!), StripFragment(next), StringComparison.Ordinal);
        }

        /// <summary>
        /// Força uma carga nova trocando a query (<c>?bfr=…</c>). A identidade continua só no
        /// fragmento; a URL do embed não usa query.
        /// </summary>
        public static string WithReloadToken(string url, long token)
        {
            var hash = url.IndexOf('#');
            var head = hash >= 0 ? url.Substring(0, hash) : url;
            var fragment = hash >= 0 ? url.Substring(hash) : string.Empty;
            var q = head.IndexOf('?');
            if (q >= 0) head = head.Substring(0, q);
            return head + "?bfr=" + token.ToString(System.Globalization.CultureInfo.InvariantCulture) + fragment;
        }

        private static string StripFragment(string url)
        {
            var hash = url.IndexOf('#');
            return hash >= 0 ? url.Substring(0, hash) : url;
        }

        private static KeyValuePair<string, string> Kv(string k, string v) => new KeyValuePair<string, string>(k, v);
    }
}
