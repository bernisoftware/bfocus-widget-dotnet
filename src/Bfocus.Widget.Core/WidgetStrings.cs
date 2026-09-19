namespace Bfocus.Widget
{
    /// <summary>Os poucos textos que o pacote mostra fora da WebView, nos três idiomas do widget.</summary>
    public sealed class WidgetStrings
    {
        private WidgetStrings() { }

        public string NotificationTitle { get; private set; } = string.Empty;
        public string NotificationBody { get; private set; } = string.Empty;
        public string Loading { get; private set; } = string.Empty;
        public string OfflineTitle { get; private set; } = string.Empty;
        public string OfflineBody { get; private set; } = string.Empty;
        public string Retry { get; private set; } = string.Empty;
        public string LauncherName { get; private set; } = string.Empty;
        public string LauncherCloseName { get; private set; } = string.Empty;
        public string ReleaseBadgeName { get; private set; } = string.Empty;
        public string PanelTitle { get; private set; } = string.Empty;
        public string ReleaseNotesTitle { get; private set; } = string.Empty;
        public string SaveAsTitle { get; private set; } = string.Empty;
        public string DownloadFailed { get; private set; } = string.Empty;

        private static readonly WidgetStrings Pt = new WidgetStrings
        {
            NotificationTitle = "Suporte",
            NotificationBody = "Há novidades nos seus chamados.",
            Loading = "Carregando...",
            OfflineTitle = "Sem conexão",
            OfflineBody = "Verifique sua internet e tente de novo.",
            Retry = "Tentar de novo",
            LauncherName = "Abrir chamados",
            LauncherCloseName = "Fechar chamados",
            ReleaseBadgeName = "Novidades e versão",
            PanelTitle = "Suporte",
            ReleaseNotesTitle = "Novidades",
            SaveAsTitle = "Salvar anexo",
            DownloadFailed = "Não foi possível baixar o arquivo.",
        };

        private static readonly WidgetStrings En = new WidgetStrings
        {
            NotificationTitle = "Support",
            NotificationBody = "There are updates on your tickets.",
            Loading = "Loading...",
            OfflineTitle = "No connection",
            OfflineBody = "Check your internet connection and try again.",
            Retry = "Try again",
            LauncherName = "Open support",
            LauncherCloseName = "Close support",
            ReleaseBadgeName = "What's new and version",
            PanelTitle = "Support",
            ReleaseNotesTitle = "What's new",
            SaveAsTitle = "Save attachment",
            DownloadFailed = "The file could not be downloaded.",
        };

        private static readonly WidgetStrings Es = new WidgetStrings
        {
            NotificationTitle = "Soporte",
            NotificationBody = "Hay novedades en tus tickets.",
            Loading = "Cargando...",
            OfflineTitle = "Sin conexión",
            OfflineBody = "Revisa tu conexión a internet e inténtalo de nuevo.",
            Retry = "Reintentar",
            LauncherName = "Abrir soporte",
            LauncherCloseName = "Cerrar soporte",
            ReleaseBadgeName = "Novedades y versión",
            PanelTitle = "Soporte",
            ReleaseNotesTitle = "Novedades",
            SaveAsTitle = "Guardar adjunto",
            DownloadFailed = "No se pudo descargar el archivo.",
        };

        /// <summary>Textos do idioma (<c>pt_BR</c> | <c>en</c> | <c>es</c>; outro valor passa pelo mapeamento).</summary>
        public static WidgetStrings For(string? locale)
        {
            switch (LocaleResolver.Normalize(locale ?? Protocol.DefaultLocale))
            {
                case "en": return En;
                case "es": return Es;
                default: return Pt;
            }
        }
    }
}
