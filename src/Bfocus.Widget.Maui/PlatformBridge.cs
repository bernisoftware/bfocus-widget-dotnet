using System;
using Microsoft.Maui;

namespace Bfocus.Widget.Maui
{
    /// <summary>
    /// Canal nativo da WebView de cada plataforma (tabela da §2 do protocolo):
    /// Android <c>bFocusAndroid</c> (addWebMessageListener), iOS/Mac <c>webkit.messageHandlers.bfocus</c>,
    /// Windows <c>chrome.webview</c>. Todos conferem a origem do embed.
    /// </summary>
    internal abstract class PlatformBridge : IDisposable
    {
        /// <summary>Executa o script (canal pacote → embed).</summary>
        public abstract void Evaluate(string script);

        public abstract void Dispose();

        public static PlatformBridge? Create(IElementHandler handler, BFocusWidget widget, WidgetSurface surface)
        {
#if ANDROID
            return handler.PlatformView is Android.Webkit.WebView v && handler is Microsoft.Maui.Handlers.IWebViewHandler wh
                ? new AndroidBridge(v, wh, widget, surface) : null;
#elif IOS || MACCATALYST
            return handler.PlatformView is WebKit.WKWebView v ? new AppleBridge(v, widget, surface) : null;
#elif WINDOWS
            return handler.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 v ? new WindowsBridge(v, widget, surface) : null;
#else
            return null;
#endif
        }

        /// <summary>A plataforma tem WebView com o canal exigido pelo protocolo.</summary>
        public static bool IsAvailable()
        {
#if ANDROID
            // Sem WebMessageListener (WebView antiga) não há canal seguro: modo navegador.
            return AndroidX.WebKit.WebViewFeature.IsFeatureSupported(AndroidX.WebKit.WebViewFeature.WebMessageListener);
#elif WINDOWS
            try
            {
                return !string.IsNullOrEmpty(Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch (Exception)
            {
                return false;
            }
#else
            return true;
#endif
        }

        /// <summary>Mensagem só da origem do embed e só do documento principal.</summary>
        protected static void Deliver(BFocusWidget widget, WidgetSurface surface, string origin, string? json)
        {
            if (json == null) return;
            var expected = widget.Identity?.Origin;
            if (expected == null || !expected.IsSameOrigin(origin)) return;
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => widget.HandleEmbedMessage(surface, origin, json));
        }
    }
}
