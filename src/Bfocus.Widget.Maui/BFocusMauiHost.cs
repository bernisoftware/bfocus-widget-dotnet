using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Bfocus.Widget.Maui
{
    /// <summary>
    /// Host .NET MAUI do <see cref="BFocusWidget"/>: telas em páginas modais de tela cheia, com a
    /// ponte nativa de cada plataforma, downloads, consulta só em primeiro plano (mobile) e modo
    /// navegador quando não há WebView utilizável.
    /// </summary>
    /// <example>
    /// <code>
    /// var widget = BFocusMauiHost.CreateWidget();          // na thread de UI (ex.: App ou MainPage)
    /// await widget.InitAsync(new BFocusConfig { ... });
    /// await widget.RegisterPushTokenAsync(fcmToken);        // mobile
    /// if (widget.HandlePush(message.Data)) return;          // ao tocar numa notificação
    /// </code>
    /// </example>
    public sealed class BFocusMauiHost : IBFocusWidgetHost
    {
        private readonly Dictionary<WidgetSurface, BFocusWebViewPage> _pages = new Dictionary<WidgetSurface, BFocusWebViewPage>();
        private readonly HashSet<Window> _tracked = new HashSet<Window>();
        private BFocusWidget? _widget;

        /// <summary>Cria o widget já com este host (chame na thread de UI).</summary>
        public static BFocusWidget CreateWidget(BFocusWidgetOptions? options = null) =>
            new BFocusWidget(new BFocusMauiHost(), options);

        /// <summary>
        /// Notificação do sistema (desktop, <c>notifications=true</c>): título, corpo e o que fazer ao
        /// clicar. Padrão: notificação local no Mac Catalyst; nas outras plataformas, nada (no
        /// mobile o aviso vem pelo push).
        /// </summary>
        public Action<string, string, Action>? NotificationHandler { get; set; }

        /// <summary>Força (<c>false</c>) o modo navegador, ou ignora a checagem (<c>true</c>).</summary>
        public bool? WebViewAvailableOverride { get; set; }

        public void Attach(BFocusWidget widget)
        {
            if (_widget != null && !ReferenceEquals(_widget, widget))
                throw new InvalidOperationException("Um BFocusMauiHost atende um só BFocusWidget.");
            _widget = widget;
            TrackCurrentWindow();
        }

        public bool IsWebViewAvailable => WebViewAvailableOverride ?? PlatformBridge.IsAvailable();

        private BFocusWidget Widget => _widget ?? throw new InvalidOperationException("Host sem widget.");

        private static INavigation? Navigation => Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;

        /// <summary>
        /// Mobile: a consulta ao launcher-state só roda com o app em primeiro plano. O host acompanha a
        /// primeira janela sozinho; chame para outras janelas.
        /// </summary>
        public void TrackWindow(Window window)
        {
#if ANDROID || IOS
            if (window == null || !_tracked.Add(window)) return;
            window.Stopped += (_, _) => _widget?.SetAppForeground(false);
            window.Resumed += (_, _) => _widget?.SetAppForeground(true);
#endif
        }

        private void TrackCurrentWindow()
        {
            var w = Application.Current?.Windows.FirstOrDefault();
            if (w != null) TrackWindow(w);
        }

        public void ShowSurface(WidgetSurface surface, string url, bool forceLoad)
        {
            TrackCurrentWindow();
            if (!_pages.TryGetValue(surface, out var page))
            {
                page = new BFocusWebViewPage(Widget, surface);
                _pages[surface] = page;
            }
            if (forceLoad || !page.HasContent) page.Navigate(url);
            var nav = Navigation;
            if (nav == null || nav.ModalStack.Contains(page)) return;
            _ = Run(() => nav.PushModalAsync(page, animated: surface != WidgetSurface.ReleaseNotesBanner));
        }

        public void HideSurface(WidgetSurface surface)
        {
            if (!_pages.TryGetValue(surface, out var page)) return;
            _ = PopAsync(page);
        }

        public void DestroySurface(WidgetSurface surface)
        {
            if (!_pages.TryGetValue(surface, out var page)) return;
            _pages.Remove(surface);
            _ = PopAsync(page, release: true);
        }

        private static async Task PopAsync(BFocusWebViewPage page, bool release = false)
        {
            var nav = Navigation;
            try
            {
                if (nav != null && nav.ModalStack.Count > 0 && ReferenceEquals(nav.ModalStack[nav.ModalStack.Count - 1], page))
                    await nav.PopModalAsync(animated: true);
            }
            finally
            {
                if (release) page.Release();
            }
        }

        public void ExecuteScript(WidgetSurface surface, string script)
        {
            if (_pages.TryGetValue(surface, out var page)) page.ExecuteScript(script);
        }

        public void MarkReady(WidgetSurface surface)
        {
            if (_pages.TryGetValue(surface, out var page)) page.MarkReady();
        }

        public void OpenExternal(string url)
        {
            if (!EmbedOrigin.IsExternalSafe(url)) return;
            _ = Run(() => Launcher.Default.OpenAsync(new Uri(url)));
        }

        public void Download(string url, string fileName) => _ = Run(() => Downloads.SaveAsync(url, fileName));

        public void ShowNotification(string title, string body, Action onClick)
        {
            if (NotificationHandler != null)
            {
                NotificationHandler(title, body, onClick);
                return;
            }
#if MACCATALYST
            var center = UserNotifications.UNUserNotificationCenter.Current;
            center.RequestAuthorization(UserNotifications.UNAuthorizationOptions.Alert | UserNotifications.UNAuthorizationOptions.Sound, (granted, _) =>
            {
                if (!granted) return;
                var content = new UserNotifications.UNMutableNotificationContent { Title = title, Body = body };
                center.AddNotificationRequest(UserNotifications.UNNotificationRequest.FromIdentifier("bfocus-activity", content, null), null);
            });
#endif
        }

        public void ClearWebViewData(WidgetIdentity identity)
        {
#if ANDROID
            Android.Webkit.WebStorage.Instance?.DeleteOrigin(identity.Origin.Value);
#elif IOS || MACCATALYST
            var host = new Uri(identity.Origin.Value).Host;
            var store = WebKit.WKWebsiteDataStore.DefaultDataStore;
            var types = WebKit.WKWebsiteDataStore.AllWebsiteDataTypes;
            store.FetchDataRecordsOfTypes(types, records =>
            {
                var match = Foundation.NSArray.FromArray<WebKit.WKWebsiteDataRecord>(records)
                    .Where(r => r.DisplayName == host || host.EndsWith("." + r.DisplayName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (match.Length > 0) store.RemoveDataOfTypes(types, match, () => { });
            });
#else
            // Windows: as WebViews já foram descartadas no logout; o WinUI não expõe a limpeza sem uma.
            Debug.WriteLine("[bfocus] ClearWebViewData não suportado nesta plataforma do MAUI");
#endif
        }

        private static async Task Run(Func<Task> action)
        {
            try
            {
                if (MainThread.IsMainThread) await action();
                else await MainThread.InvokeOnMainThreadAsync(action);
            }
            catch (Exception e)
            {
                Debug.WriteLine("[bfocus] " + e);
            }
        }
    }
}
