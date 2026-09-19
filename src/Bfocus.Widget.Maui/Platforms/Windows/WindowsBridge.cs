#if WINDOWS
using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Bfocus.Widget.Maui
{
    /// <summary>Windows (WinUI WebView2): <c>chrome.webview</c> → <c>WebMessageReceived</c>, conferindo <c>args.Source</c>.</summary>
    internal sealed class WindowsBridge : PlatformBridge
    {
        private readonly WebView2 _view;
        private readonly BFocusWidget _widget;
        private readonly WidgetSurface _surface;
        private CoreWebView2? _core;

        public WindowsBridge(WebView2 view, BFocusWidget widget, WidgetSurface surface)
        {
            _view = view;
            _widget = widget;
            _surface = surface;
            if (view.CoreWebView2 != null) Attach(view.CoreWebView2);
            else view.CoreWebView2Initialized += OnInitialized;
        }

        private void OnInitialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
        {
            if (sender.CoreWebView2 != null) Attach(sender.CoreWebView2);
        }

        private void Attach(CoreWebView2 core)
        {
            _core = core;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.WebMessageReceived += OnMessage;
            core.NewWindowRequested += OnNewWindow;
        }

        private void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            string json;
            try
            {
                json = args.TryGetWebMessageAsString();
            }
            catch (ArgumentException)
            {
                return;
            }
            Deliver(_widget, _surface, args.Source, json);
        }

        private void OnNewWindow(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            _widget.OpenExternalFromHost(args.Uri);
        }

        public override void Evaluate(string script)
        {
            if (_core != null) _ = _core.ExecuteScriptAsync(script);
        }

        public override void Dispose()
        {
            _view.CoreWebView2Initialized -= OnInitialized;
            if (_core == null) return;
            _core.WebMessageReceived -= OnMessage;
            _core.NewWindowRequested -= OnNewWindow;
        }
    }
}
#endif
