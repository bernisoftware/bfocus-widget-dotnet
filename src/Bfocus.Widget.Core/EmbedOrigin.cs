using System;

namespace Bfocus.Widget
{
    /// <summary>
    /// Origem (esquema + host + porta) de <c>embedBaseUrl</c>. É a única aceita na ponte
    /// (WebView2: <c>args.Source</c>) e a única para onde a WebView navega.
    /// </summary>
    public sealed class EmbedOrigin
    {
        private readonly string _scheme;
        private readonly string _host;
        private readonly int _port;

        public EmbedOrigin(string embedBaseUrl)
        {
            if (!Uri.TryCreate(embedBaseUrl, UriKind.Absolute, out var uri))
                throw new ArgumentException("embedBaseUrl inválida.", nameof(embedBaseUrl));
            _scheme = uri.Scheme.ToLowerInvariant();
            _host = uri.Host.ToLowerInvariant();
            _port = uri.Port;
            Value = uri.IsDefaultPort ? _scheme + "://" + _host : _scheme + "://" + _host + ":" + _port;
        }

        /// <summary>Ex.: <c>https://widget.bfocus.com.br</c> ou <c>http://127.0.0.1:8787</c>.</summary>
        public string Value { get; }

        /// <summary><c>true</c> quando a URL (documento, navegação) é da origem do embed.</summary>
        public bool IsSameOrigin(string? url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return string.Equals(uri.Scheme, _scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Host, _host, StringComparison.OrdinalIgnoreCase)
                && uri.Port == _port;
        }

        /// <summary>Links que podem ir ao navegador do sistema (nada de file:, javascript:, data:…).</summary>
        public static bool IsExternalSafe(string? url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp ||
                   uri.Scheme == Uri.UriSchemeMailto || uri.Scheme == "tel";
        }

        /// <summary>Downloads só por http(s).</summary>
        public static bool IsDownloadSafe(string? url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
        }

        public override string ToString() => Value;
    }
}
