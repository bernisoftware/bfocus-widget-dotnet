using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace Bfocus.Widget.Maui
{
    /// <summary>
    /// Uma tela do widget em página modal de tela cheia: WebView do MAUI com a ponte nativa,
    /// "carregando" até o <c>bfocus:ready</c> e "sem conexão" com "tentar de novo". A WebView é
    /// mantida entre aberturas.
    /// </summary>
    public class BFocusWebViewPage : ContentPage
    {
        private readonly BFocusWidget _widget;
        private readonly WidgetSurface _surface;
        private readonly WebView _web;
        private readonly View _loading;
        private readonly View _offline;
        private IDispatcherTimer? _fallback;
        private PlatformBridge? _bridge;
        private string? _url;
        private string? _pending;

        public BFocusWebViewPage(BFocusWidget widget, WidgetSurface surface)
        {
            _widget = widget ?? throw new ArgumentNullException(nameof(widget));
            _surface = surface;
            var strings = widget.Strings;
            Title = surface == WidgetSurface.Tickets ? strings.PanelTitle : strings.ReleaseNotesTitle;
            BackgroundColor = Colors.White;
            // Tela cheia de verdade no iOS: sem o gesto de puxar para baixo (o banner não fecha por gesto).
            On<iOS>().SetModalPresentationStyle(UIModalPresentationStyle.FullScreen);

            _web = new WebView();
            // Não desconecta o handler ao sair da pilha modal: a WebView fica viva para reabrir rápido.
            HandlerProperties.SetDisconnectPolicy(_web, HandlerDisconnectPolicy.Manual);
            _web.HandlerChanged += OnHandlerChanged;
            _web.Navigating += OnNavigating;
            _web.Navigated += OnNavigated;

            var muted = Color.FromArgb("#64748B");
            _loading = new VerticalStackLayout
            {
                BackgroundColor = Colors.White, VerticalOptions = LayoutOptions.Fill, HorizontalOptions = LayoutOptions.Fill, Spacing = 12,
                Children =
                {
                    new ActivityIndicator { IsRunning = true, Color = muted, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 200, 0, 0) },
                    new Label { Text = strings.Loading, TextColor = muted, HorizontalOptions = LayoutOptions.Center },
                },
            };
            var retry = new Button { Text = strings.Retry, HorizontalOptions = LayoutOptions.Center };
            retry.Clicked += (_, _) => Retry();
            _offline = new VerticalStackLayout
            {
                BackgroundColor = Colors.White, IsVisible = false, Padding = new Thickness(32), Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = strings.OfflineTitle, FontSize = 18, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, TextColor = Color.FromArgb("#0F172A") },
                    new Label { Text = strings.OfflineBody, TextColor = muted, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 12) },
                    retry,
                },
            };
            Content = new Grid { Children = { _web, _loading, _offline } };
        }

        public WidgetSurface Surface => _surface;

        public bool HasContent => _url != null;

        public void Navigate(string url)
        {
            _url = url;
            ShowLoading();
            if (_bridge == null)
            {
                // Android: o canal bFocusAndroid precisa estar registrado antes da carga.
                _pending = url;
                return;
            }
            Load(url);
        }

        private void Load(string url)
        {
            var current = (_web.Source as UrlWebViewSource)?.Url;
            _web.Source = new UrlWebViewSource
            {
                Url = EmbedUrl.IsSameDocument(current, url) ? EmbedUrl.WithReloadToken(url, DateTime.UtcNow.Ticks) : url,
            };
        }

        public void ExecuteScript(string script) => _bridge?.Evaluate(script);

        public void MarkReady()
        {
            _fallback?.Stop();
            _loading.IsVisible = false;
            _offline.IsVisible = false;
        }

        /// <summary>Solta a WebView nativa (logout, identidade nova, banner concluído).</summary>
        public void Release()
        {
            _fallback?.Stop();
            _bridge?.Dispose();
            _bridge = null;
            _web.Handler?.DisconnectHandler();
        }

        private void OnHandlerChanged(object? sender, EventArgs e)
        {
            _bridge?.Dispose();
            _bridge = null;
            if (_web.Handler == null || _web.Handler.PlatformView == null) return;
            _bridge = PlatformBridge.Create(_web.Handler, _widget, _surface);
            if (_bridge != null && _pending != null)
            {
                var url = _pending;
                _pending = null;
                Load(url);
            }
        }

        private void OnNavigating(object? sender, WebNavigatingEventArgs e)
        {
            if (e.Url == "about:blank") return;
            var origin = _widget.Identity?.Origin;
            if (origin == null || !origin.IsSameOrigin(e.Url))
            {
                // A WebView só navega dentro do embed; o resto vai para o navegador do sistema.
                e.Cancel = true;
                _widget.OpenExternalFromHost(e.Url);
            }
        }

        private void OnNavigated(object? sender, WebNavigatedEventArgs e)
        {
            if (e.Result == WebNavigationResult.Success)
            {
                _bridge?.Evaluate(HostScript.Flush);
                _fallback ??= CreateFallback();
                _fallback.Stop();
                _fallback.Start();
                return;
            }
            if (e.Result == WebNavigationResult.Cancel) return; // nós cancelamos (navegação para fora)
            ShowOffline();
        }

        private IDispatcherTimer CreateFallback()
        {
            // Se o ready não vier, mostra a página mesmo assim.
            var t = Dispatcher.CreateTimer();
            t.Interval = TimeSpan.FromSeconds(8);
            t.IsRepeating = false;
            t.Tick += (_, _) => _loading.IsVisible = false;
            return t;
        }

        private void ShowLoading()
        {
            _offline.IsVisible = false;
            _loading.IsVisible = true;
        }

        private void ShowOffline()
        {
            _fallback?.Stop();
            _loading.IsVisible = false;
            _offline.IsVisible = true;
        }

        private void Retry()
        {
            if (_url == null) return;
            _widget.NotifySurfaceReloading(_surface);
            Navigate(_url);
        }

        /// <summary>Voltar do Android: fecha chamados/histórico pelo widget; o banner não fecha.</summary>
        protected override bool OnBackButtonPressed()
        {
            if (_surface != WidgetSurface.ReleaseNotesBanner) _widget.NotifySurfaceClosedByUser(_surface);
            return true;
        }
    }
}
