namespace Bfocus.Widget
{
    /// <summary>Constantes do Host Protocol v1 (docs/widget-host-protocol-v1.md).</summary>
    public static class Protocol
    {
        /// <summary>Versão do Host Protocol falada por este pacote (<c>hp=1</c>).</summary>
        public const int HostProtocol = 1;

        /// <summary>Versão do pacote. Tem de bater com &lt;Version&gt; do Directory.Build.props (há teste).</summary>
        public const string PackageVersion = "0.1.1";

        /// <summary>Família do header <c>X-bFocus-Client</c> para .NET.</summary>
        public const string ClientFamily = "dotnet";

        /// <summary>Valor do header <c>X-bFocus-Client</c> e do parâmetro <c>client</c>.</summary>
        public const string Client = ClientFamily + "/" + PackageVersion;

        public const string DefaultApiBaseUrl = "https://api.bfocus.com.br";
        public const string DefaultEmbedBaseUrl = "https://widget.bfocus.com.br/v1";
        public const int DefaultPollIntervalSeconds = 60;
        public const string DefaultLocale = "pt_BR";

        /// <summary>Cor da marca bFocus quando o tenant não manda a sua (tokens.brandFallback).</summary>
        public const string BrandFallbackColor = "#6366F1";

        /// <summary>Tempo máximo de cada chamada ao bFocus.</summary>
        public static readonly System.TimeSpan RequestTimeout = System.TimeSpan.FromSeconds(15);

        public const string HeaderKey = "X-bFocus-Widget-Key";
        public const string HeaderUser = "X-bFocus-Widget-User";
        public const string HeaderParentOrigin = "X-bFocus-Parent-Origin";
        public const string HeaderClient = "X-bFocus-Client";

        public const string ErrorUserHashInvalid = "WIDGET_USER_HASH_INVALID";
        public const string ErrorVerifiedSessionRequired = "WIDGET_VERIFIED_SESSION_REQUIRED";
        public const string ErrorConfigFailed = "WIDGET_CONFIG_FAILED";

        /// <summary>Erros próprios do pacote (não vêm do servidor).</summary>
        public const string ErrorUserHashProviderFailed = "WIDGET_USER_HASH_PROVIDER_FAILED";
        public const string ErrorHostProtocolMismatch = "WIDGET_HOST_PROTOCOL_MISMATCH";
        public const string ErrorDownloadFailed = "WIDGET_DOWNLOAD_FAILED";
    }

    /// <summary>Tipos de mensagem da ponte (§3 do protocolo).</summary>
    public static class MessageTypes
    {
        // embed → pacote
        public const string Ready = "bfocus:ready";
        public const string Close = "bfocus:close";
        public const string Unread = "bfocus:unread";
        public const string Seen = "bfocus:seen";
        public const string Branding = "bfocus:branding";
        public const string Error = "bfocus:error";
        public const string OpenExternal = "bfocus:openExternal";
        public const string Download = "bfocus:download";
        public const string RnBranding = "bfocus:rn:branding";
        public const string RnDone = "bfocus:rn:done";
        public const string RnCloseHistory = "bfocus:rn:closeHistory";

        // pacote → embed
        public const string Open = "bfocus:open";
        public const string Navigate = "bfocus:navigate";
    }
}
