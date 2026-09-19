// Compilado (como link) em Bfocus.Widget.WinForms e Bfocus.Widget.Wpf: liga um CoreWebView2 ao
// BFocusWidget seguindo o Host Protocol v1 (ponte, navegação travada, downloads, permissões).
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Web.WebView2.Core;

namespace Bfocus.Widget.Hosting
{
    internal sealed class WebView2Bridge : IDisposable
    {
        private readonly CoreWebView2 _core;
        private readonly BFocusWidget _widget;
        private readonly WidgetSurface _surface;
        private readonly Func<string, string?> _askSavePath;
        private readonly Action _onLoading;
        private readonly Action _onLoaded;
        private readonly Action _onFailed;

        /// <param name="core">WebView2 já inicializada.</param>
        /// <param name="widget">Widget que recebe as mensagens da ponte.</param>
        /// <param name="surface">Tela hospedada por esta WebView.</param>
        /// <param name="askSavePath">Mostra "salvar como" com o nome sugerido; <c>null</c> = cancelado.</param>
        /// <param name="onLoading">Navegação da página do embed começou (mostra "carregando").</param>
        /// <param name="onLoaded">Página carregou (o "carregando" some no bfocus:ready ou por tempo).</param>
        /// <param name="onFailed">Sem rede / erro de carregamento / processo caiu (tela "sem conexão").</param>
        public WebView2Bridge(CoreWebView2 core, BFocusWidget widget, WidgetSurface surface,
            Func<string, string?> askSavePath, Action onLoading, Action onLoaded, Action onFailed)
        {
            _core = core;
            _widget = widget;
            _surface = surface;
            _askSavePath = askSavePath;
            _onLoading = onLoading;
            _onLoaded = onLoaded;
            _onFailed = onFailed;

            var s = core.Settings;
            s.IsWebMessageEnabled = true;
            s.AreHostObjectsAllowed = false; // a ponte é só por mensagens, nada de objeto .NET no JS
            s.IsStatusBarEnabled = false;
            s.IsZoomControlEnabled = false;
            s.AreDevToolsEnabled = Debugger.IsAttached;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;

            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.NewWindowRequested += OnNewWindowRequested;
            core.DownloadStarting += OnDownloadStarting;
            core.PermissionRequested += OnPermissionRequested;
            core.ProcessFailed += OnProcessFailed;
        }

        public void ExecuteScript(string script)
        {
            try
            {
                _ = _core.ExecuteScriptAsync(script);
            }
            catch (Exception e) when (e is InvalidOperationException || e is ObjectDisposedException)
            {
                // WebView fechada no meio do caminho: a mensagem perde o sentido.
            }
        }

        private EmbedOrigin? Origin => _widget.Identity?.Origin;

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // Segurança da ponte: só a origem do embed fala com o pacote (args.Source).
            var origin = Origin;
            if (origin == null || !origin.IsSameOrigin(e.Source)) return;
            string json;
            try
            {
                json = e.TryGetWebMessageAsString();
            }
            catch (ArgumentException)
            {
                return; // o protocolo manda string JSON; objeto cru é ignorado
            }
            _widget.HandleEmbedMessage(_surface, e.Source, json);
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var origin = Origin;
            if (e.Uri == "about:blank") return;
            if (origin == null || !origin.IsSameOrigin(e.Uri))
            {
                // A WebView só navega dentro do embed; o resto vai para o navegador do sistema.
                e.Cancel = true;
                if (e.IsUserInitiated || !e.IsRedirected) _widget.OpenExternalFromHost(e.Uri);
                return;
            }
            _onLoading();
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                _onLoaded();
                // Canal já existe (chrome.webview), mas força a entrega do que o embed guardou.
                ExecuteScript(HostScript.Flush);
                return;
            }
            if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return; // nós cancelamos
            _onFailed();
        }

        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true; // nunca abre janela do WebView2
            _widget.OpenExternalFromHost(e.Uri);
        }

        private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        {
            // <a download> na página: "salvar como" do Windows em vez da barra de downloads do Edge.
            var deferral = e.GetDeferral();
            var suggested = FileNames.Sanitize(Path.GetFileName(e.ResultFilePath));
            var ctx = SynchronizationContext.Current;
            void Ask()
            {
                try
                {
                    var path = _askSavePath(suggested);
                    if (path == null) e.Cancel = true;
                    else
                    {
                        e.ResultFilePath = path;
                        e.Handled = true;
                    }
                }
                finally
                {
                    deferral.Complete();
                }
            }
            // O diálogo sai do handler (evita reentrância no evento do WebView2).
            if (ctx != null) ctx.Post(_ => Ask(), null);
            else Ask();
        }

        private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            switch (e.PermissionKind)
            {
                case CoreWebView2PermissionKind.Camera:
                case CoreWebView2PermissionKind.Microphone:
                case CoreWebView2PermissionKind.Geolocation:
                case CoreWebView2PermissionKind.Notifications:
                case CoreWebView2PermissionKind.OtherSensors:
                    e.State = CoreWebView2PermissionState.Deny; // o widget não usa
                    break;
            }
        }

        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                _widget.NotifySurfaceLost(_surface);
            _onFailed();
        }

        public void Dispose()
        {
            _core.WebMessageReceived -= OnWebMessageReceived;
            _core.NavigationStarting -= OnNavigationStarting;
            _core.NavigationCompleted -= OnNavigationCompleted;
            _core.NewWindowRequested -= OnNewWindowRequested;
            _core.DownloadStarting -= OnDownloadStarting;
            _core.PermissionRequested -= OnPermissionRequested;
            _core.ProcessFailed -= OnProcessFailed;
        }
    }
}
