using System;

namespace Bfocus.Widget
{
    /// <summary>As três telas que o pacote hospeda numa WebView.</summary>
    public enum WidgetSurface
    {
        /// <summary><c>embed.html</c>: chamados e chat (painel 400 × 620).</summary>
        Tickets,
        /// <summary><c>release-notes.html#view=history</c>: histórico de versões.</summary>
        ReleaseNotesHistory,
        /// <summary><c>release-notes.html#view=banner</c>: modal de ciência, sem fechar pelo usuário.</summary>
        ReleaseNotesBanner,
    }

    /// <summary>
    /// O que cada toolkit (WinForms, WPF, MAUI) implementa para o <see cref="BFocusWidget"/>. O núcleo
    /// decide o quê e quando; o host só mostra WebViews e fala com o sistema.
    /// <para>
    /// O núcleo chama estes métodos pelo <see cref="System.Threading.SynchronizationContext"/> capturado
    /// ao criar o widget (a thread de UI). O host devolve as mensagens da ponte com
    /// <see cref="BFocusWidget.HandleEmbedMessage"/>, depois de conferir a origem.
    /// </para>
    /// </summary>
    public interface IBFocusWidgetHost
    {
        /// <summary>Chamado uma vez pelo construtor do <see cref="BFocusWidget"/>.</summary>
        void Attach(BFocusWidget widget);

        /// <summary><c>false</c> sem WebView utilizável (ex.: sem runtime do WebView2): o widget cai no modo navegador.</summary>
        bool IsWebViewAvailable { get; }

        /// <summary>
        /// Mostra a tela. Com <paramref name="forceLoad"/> (ou sem WebView ainda), navega para
        /// <paramref name="url"/>; senão só mostra a WebView mantida.
        /// </summary>
        void ShowSurface(WidgetSurface surface, string url, bool forceLoad);

        /// <summary>Esconde mantendo a WebView (reabrir é instantâneo).</summary>
        void HideSurface(WidgetSurface surface);

        /// <summary>Descarta a WebView (logout, identidade nova, banner concluído).</summary>
        void DestroySurface(WidgetSurface surface);

        /// <summary>Executa o script (canal pacote → embed) na WebView da tela, se existir.</summary>
        void ExecuteScript(WidgetSurface surface, string script);

        /// <summary>O embed mandou <c>bfocus:ready</c>: esconda o "carregando".</summary>
        void MarkReady(WidgetSurface surface);

        /// <summary>Abre no navegador do sistema (<c>bfocus:openExternal</c>, navegação para fora).</summary>
        void OpenExternal(string url);

        /// <summary>Baixa o arquivo com o nome dado (salvar como / gerenciador de downloads).</summary>
        void Download(string url, string fileName);

        /// <summary>Notificação do sistema; <paramref name="onClick"/> abre o widget.</summary>
        void ShowNotification(string title, string body, Action onClick);

        /// <summary>
        /// Apaga os dados da WebView (localStorage, cookies…) da origem do embed (logout). Recebe a
        /// identidade que saiu: <see cref="WidgetIdentity.Origin"/> e <see cref="WidgetIdentity.AppId"/>
        /// (pasta de dados da WebView).
        /// </summary>
        void ClearWebViewData(WidgetIdentity identity);
    }
}
