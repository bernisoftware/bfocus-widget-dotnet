using System;

namespace Bfocus.Widget
{
    /// <summary>
    /// Retrato imutável da identidade usada nas chamadas e na URL do embed: config validada +
    /// idioma resolvido + <c>userHash</c> efetivo + <c>client</c>.
    /// </summary>
    public sealed class WidgetIdentity
    {
        private WidgetIdentity(BFocusConfig config, string client, string? userHash)
        {
            PublishableKey = config.PublishableKey.Trim();
            AppId = config.AppId.Trim();
            ParentOrigin = "app://" + AppId.ToLowerInvariant();
            Client = client;
            UserHash = string.IsNullOrEmpty(userHash) ? null : userHash;
            Locale = LocaleResolver.Resolve(config.Locale);
            Product = string.IsNullOrEmpty(config.Product) ? null : config.Product;
            Audience = string.IsNullOrEmpty(config.Audience) ? null : config.Audience;
            ApiBaseUrl = TrimSlash(config.ApiBaseUrl);
            EmbedBaseUrl = TrimSlash(config.EmbedBaseUrl);
            ShowReleaseNotes = config.ShowReleaseNotes;
            UserExternalId = config.User.ExternalId;
            CustomerExternalId = config.Customer.ExternalId;
            UserJson = UserPayload.Json(config.User, config.Customer, UserHash, Locale);
            UserBase64 = UserPayload.Base64(UserJson);
            Origin = new EmbedOrigin(EmbedBaseUrl);
        }

        /// <summary>Monta a identidade. <paramref name="userHash"/> sobrepõe o da config (vindo do provider).</summary>
        public static WidgetIdentity Create(BFocusConfig config, string client = Protocol.Client, string? userHash = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return new WidgetIdentity(config, client, userHash ?? config.UserHash);
        }

        public string PublishableKey { get; }
        public string AppId { get; }
        public string ParentOrigin { get; }
        public string Client { get; }
        public string? UserHash { get; }
        public string Locale { get; }
        public string? Product { get; }
        public string? Audience { get; }
        public string ApiBaseUrl { get; }
        public string EmbedBaseUrl { get; }
        public bool ShowReleaseNotes { get; }
        public string UserExternalId { get; }
        public string CustomerExternalId { get; }

        /// <summary>JSON compacto do usuário (antes do base64).</summary>
        public string UserJson { get; }

        /// <summary>Valor de <c>user=</c> e do header <c>X-bFocus-Widget-User</c>.</summary>
        public string UserBase64 { get; }

        /// <summary>Origem do embed: única aceita na ponte e na navegação.</summary>
        public EmbedOrigin Origin { get; }

        /// <summary>Chave do <c>last_seen</c>: publishableKey + user + customer (igual ao widget web).</summary>
        public string LastSeenStorageKey => "bf:lastSeen:" + PublishableKey + ":" + UserExternalId + ":" + CustomerExternalId;

        /// <summary>Mesma pessoa no mesmo tenant (o que decide recriar a WebView).</summary>
        public bool IsSamePerson(WidgetIdentity? other) =>
            other != null && other.PublishableKey == PublishableKey && other.UserExternalId == UserExternalId &&
            other.CustomerExternalId == CustomerExternalId && other.EmbedBaseUrl == EmbedBaseUrl;

        internal WidgetIdentity WithUserHash(BFocusConfig config, string? userHash) => new WidgetIdentity(config, Client, userHash);

        private static string TrimSlash(string s) => (s ?? string.Empty).Trim().TrimEnd('/');
    }
}
