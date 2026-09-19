using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bfocus.Widget
{
    /// <summary>Usuário final do app do integrador.</summary>
    public sealed class BFocusUser
    {
        /// <summary>Id do usuário no sistema do integrador (obrigatório).</summary>
        public string ExternalId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    /// <summary>Cliente (empresa) a que o usuário pertence.</summary>
    public sealed class BFocusCustomer
    {
        /// <summary>Id do cliente no sistema do integrador (obrigatório).</summary>
        public string ExternalId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Document { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
    }

    /// <summary>Configuração do <see cref="BFocusWidget.InitAsync"/> (tabela da §3 do BRIEF).</summary>
    public sealed class BFocusConfig
    {
        /// <summary>Chave pública <c>bf_pk_…</c>. Nunca coloque <c>bf_whs_</c>, <c>bf_live_</c> ou <c>bf_sk_</c> no app.</summary>
        public string PublishableKey { get; set; } = string.Empty;

        /// <summary>Id do app cadastrado em Integrações → Apps nativos. A origem vira <c>app://&lt;appId em minúsculas&gt;</c>.</summary>
        public string AppId { get; set; } = string.Empty;

        public BFocusUser User { get; set; } = new BFocusUser();
        public BFocusCustomer Customer { get; set; } = new BFocusCustomer();

        /// <summary>HMAC calculado no SERVIDOR do integrador (<c>sign_widget_identity</c> dos SDKs).</summary>
        public string? UserHash { get; set; }

        /// <summary>
        /// Busca o <see cref="UserHash"/> no servidor do integrador. Chamado no init e de novo após
        /// <c>WIDGET_USER_HASH_INVALID</c>.
        /// </summary>
        public Func<CancellationToken, Task<string?>>? UserHashProvider { get; set; }

        /// <summary>Slug do produto (release notes e launcher-state).</summary>
        public string? Product { get; set; }

        /// <summary><c>external</c> | <c>internal</c> | <c>both</c>.</summary>
        public string? Audience { get; set; }

        /// <summary><c>pt_BR</c> | <c>en</c> | <c>es</c>. Vazio = idioma do sistema (pt* → pt_BR, es* → es, resto → en).</summary>
        public string? Locale { get; set; }

        /// <summary><c>false</c> esconde o splash de novidades dentro dos chamados.</summary>
        public bool ShowReleaseNotes { get; set; } = true;

        /// <summary>Abre sozinho o banner de ciência quando o launcher-state manda <c>banner_ids</c>.</summary>
        public bool AutoShowReleaseBanner { get; set; } = true;

        public string ApiBaseUrl { get; set; } = Protocol.DefaultApiBaseUrl;
        public string EmbedBaseUrl { get; set; } = Protocol.DefaultEmbedBaseUrl;

        /// <summary>Intervalo da consulta ao launcher-state com o widget fechado (mínimo 5 s).</summary>
        public int PollIntervalSeconds { get; set; } = Protocol.DefaultPollIntervalSeconds;

        /// <summary>Desktop: notificação do sistema quando o "•" acende.</summary>
        public bool Notifications { get; set; }

        /// <summary>Confere a configuração e lança <see cref="ArgumentException"/> no primeiro problema.</summary>
        public void Validate()
        {
            var key = (PublishableKey ?? string.Empty).Trim();
            if (key.StartsWith("bf_whs_", StringComparison.Ordinal) ||
                key.StartsWith("bf_live_", StringComparison.Ordinal) ||
                key.StartsWith("bf_sk_", StringComparison.Ordinal))
            {
                // Segredo no app vaza para qualquer um que abra o binário.
                throw new ArgumentException(
                    "Nunca use segredos (bf_whs_, bf_live_, bf_sk_) no app: use a chave pública bf_pk_… e calcule o userHash no servidor.",
                    nameof(PublishableKey));
            }
            if (!key.StartsWith("bf_pk_", StringComparison.Ordinal))
                throw new ArgumentException("publishableKey precisa ser a chave pública bf_pk_….", nameof(PublishableKey));
            if (string.IsNullOrWhiteSpace(AppId))
                throw new ArgumentException("appId é obrigatório no .NET (ex.: com.empresa.erp).", nameof(AppId));
            if (User == null || string.IsNullOrWhiteSpace(User.ExternalId))
                throw new ArgumentException("user.externalId é obrigatório.", nameof(User));
            if (Customer == null || string.IsNullOrWhiteSpace(Customer.ExternalId))
                throw new ArgumentException("customer.externalId é obrigatório.", nameof(Customer));
            if (UserHash != null && UserHash.StartsWith("bf_", StringComparison.Ordinal))
                throw new ArgumentException("userHash é o HMAC hexadecimal, não uma chave.", nameof(UserHash));
            if (!string.IsNullOrEmpty(Audience) && Audience != "external" && Audience != "internal" && Audience != "both")
                throw new ArgumentException("audience deve ser external, internal ou both.", nameof(Audience));
            ValidateBaseUrl(ApiBaseUrl, nameof(ApiBaseUrl));
            ValidateBaseUrl(EmbedBaseUrl, nameof(EmbedBaseUrl));
        }

        private static void ValidateBaseUrl(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
                throw new ArgumentException(name + " precisa ser uma URL absoluta.", name);
            if (uri.Scheme == Uri.UriSchemeHttps) return;
            // http:// só para testes locais (servidor simulado).
            if (uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || uri.Host == "localhost")) return;
            throw new ArgumentException(name + " precisa usar https:// (http:// só para 127.0.0.1/localhost).", name);
        }
    }
}
