#if IOS || MACCATALYST
using System;
using Foundation;
using WebKit;

namespace Bfocus.Widget.Maui
{
    /// <summary>
    /// iOS / Mac Catalyst: <c>WKScriptMessageHandler</c> com nome <c>bfocus</c>, conferindo
    /// <c>frameInfo.securityOrigin</c> (só a origem do embed, só o documento principal).
    /// </summary>
    internal sealed class AppleBridge : PlatformBridge
    {
        private const string HandlerName = "bfocus";
        private readonly WKWebView _view;
        private readonly Handler _handler;

        public AppleBridge(WKWebView view, BFocusWidget widget, WidgetSurface surface)
        {
            _view = view;
            _handler = new Handler(widget, surface);
            var controller = view.Configuration.UserContentController;
            controller.RemoveScriptMessageHandler(HandlerName); // WebView reaproveitada
            controller.AddScriptMessageHandler(_handler, HandlerName);
        }

        public override void Evaluate(string script) => _view.EvaluateJavaScript(script, (_, _) => { });

        public override void Dispose()
        {
            // O UserContentController segura o handler com referência forte: solta para não vazar.
            _view.Configuration.UserContentController.RemoveScriptMessageHandler(HandlerName);
            _handler.Dispose();
        }

        private sealed class Handler : NSObject, IWKScriptMessageHandler
        {
            private readonly BFocusWidget _widget;
            private readonly WidgetSurface _surface;

            public Handler(BFocusWidget widget, WidgetSurface surface)
            {
                _widget = widget;
                _surface = surface;
            }

            [Export("userContentController:didReceiveScriptMessage:")]
            public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
            {
                var frame = message.FrameInfo;
                if (frame == null || !frame.MainFrame) return;
                var o = frame.SecurityOrigin;
                var origin = o.Protocol + "://" + o.Host + (o.Port > 0 ? ":" + o.Port : string.Empty);
                Deliver(_widget, _surface, origin, (message.Body as NSString)?.ToString());
            }
        }
    }
}
#endif
