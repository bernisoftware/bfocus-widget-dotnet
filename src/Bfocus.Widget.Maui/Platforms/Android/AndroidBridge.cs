#if ANDROID
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.Webkit;
using AndroidX.WebKit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Storage;
using AWebView = Android.Webkit.WebView;

namespace Bfocus.Widget.Maui
{
    /// <summary>
    /// Android: <c>WebViewCompat.addWebMessageListener</c> com a origem do embed como regra (nunca
    /// <c>addJavascriptInterface</c>), seletor de arquivos e downloads pelo DownloadManager.
    /// </summary>
    internal sealed class AndroidBridge : PlatformBridge
    {
        private const string ObjectName = "bFocusAndroid";
        private readonly AWebView _view;
        private readonly Listener _listener;

        public AndroidBridge(AWebView view, IWebViewHandler handler, BFocusWidget widget, WidgetSurface surface)
        {
            _view = view;
            var s = view.Settings;
            s.JavaScriptEnabled = true;
            s.DomStorageEnabled = true; // o estado de "lido" dos chamados mora no localStorage
            s.AllowFileAccess = false;
            s.AllowContentAccess = false;
            s.SetSupportMultipleWindows(false);

            var origin = widget.Identity?.Origin.Value ?? throw new InvalidOperationException("widget sem init");
            _listener = new Listener(widget, surface);
            // Registrado antes de carregar a página: o objeto só é injetado nas próximas navegações.
            WebViewCompat.AddWebMessageListener(view, ObjectName, new HashSet<string> { origin }, _listener);
            view.SetWebChromeClient(new FileChooserClient(handler));
            view.SetDownloadListener(new DownloadListener());
        }

        public override void Evaluate(string script) => _view.EvaluateJavascript(script, null);

        public override void Dispose()
        {
            try
            {
                WebViewCompat.RemoveWebMessageListener(_view, ObjectName);
            }
            catch (Exception)
            {
                // WebView já destruída
            }
        }

        private sealed class Listener : Java.Lang.Object, WebViewCompat.IWebMessageListener
        {
            private readonly BFocusWidget _widget;
            private readonly WidgetSurface _surface;

            public Listener(BFocusWidget widget, WidgetSurface surface)
            {
                _widget = widget;
                _surface = surface;
            }

            public void OnPostMessage(AWebView? view, WebMessageCompat? message, Android.Net.Uri? sourceOrigin, bool isMainFrame, JavaScriptReplyProxy? replyProxy)
            {
                if (!isMainFrame || message == null || sourceOrigin == null) return;
                Deliver(_widget, _surface, sourceOrigin.ToString() ?? string.Empty, message.Data);
            }
        }

        /// <summary><c>&lt;input type=file multiple&gt;</c> exige <c>onShowFileChooser</c> no Android.</summary>
        private sealed class FileChooserClient : MauiWebChromeClient
        {
            public FileChooserClient(IWebViewHandler handler) : base(handler) { }

            public override bool OnShowFileChooser(AWebView? webView, IValueCallback? filePathCallback, FileChooserParams? fileChooserParams)
            {
                if (filePathCallback == null) return false;
                var multiple = fileChooserParams?.Mode == ChromeFileChooserMode.OpenMultiple;
                _ = PickAsync(filePathCallback, multiple);
                return true;
            }

            private static async Task PickAsync(IValueCallback callback, bool multiple)
            {
                Android.Net.Uri[]? uris = null;
                try
                {
                    IEnumerable<FileResult?>? files = multiple
                        ? await FilePicker.Default.PickMultipleAsync()
                        : new[] { await FilePicker.Default.PickAsync() };
                    uris = files?.Where(f => f != null)
                        .Select(f => Android.Net.Uri.FromFile(new Java.IO.File(f!.FullPath))!)
                        .ToArray();
                }
                catch (Exception)
                {
                    // cancelado ou sem permissão: devolve nada
                }
                // O WebView exige exatamente uma resposta, mesmo no cancelamento.
                callback.OnReceiveValue(uris != null && uris.Length > 0 ? Java.Lang.Object.FromArray(uris) : null);
            }
        }

        private sealed class DownloadListener : Java.Lang.Object, IDownloadListener
        {
            public void OnDownloadStart(string? url, string? userAgent, string? contentDisposition, string? mimetype, long contentLength)
            {
                if (!EmbedOrigin.IsDownloadSafe(url)) return;
                Downloads.EnqueueAndroid(url!, URLUtil.GuessFileName(url, contentDisposition, mimetype) ?? "download", mimetype);
            }
        }
    }
}
#endif
