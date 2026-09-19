using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Bfocus.Widget.Hosting;
using Microsoft.Web.WebView2.Wpf;

namespace Bfocus.Widget.Wpf
{
    /// <summary>
    /// Uma tela do widget: WebView2 com o embed, "carregando" até o <c>bfocus:ready</c> e "sem conexão"
    /// com "tentar de novo". Pode ir direto num layout próprio; o <see cref="BFocusWpfHost"/> o coloca
    /// nas janelas padrão.
    /// </summary>
    /// <remarks>
    /// A WebView2 do WPF é uma janela nativa: nada do WPF desenha por cima dela (airspace). Por isso
    /// o "carregando" e o "sem conexão" escondem a WebView em vez de cobri-la.
    /// </remarks>
    public sealed class BFocusWebViewHost : UserControl, IDisposable
    {
        private readonly BFocusWidget _widget;
        private readonly WidgetSurface _surface;
        private readonly WebView2 _web;
        private readonly Border _loading;
        private readonly Border _offline;
        private readonly DispatcherTimer _readyFallback;
        private WebView2Bridge? _bridge;
        private Task? _init;
        private string? _url;
        private bool _disposed;

        public BFocusWebViewHost(BFocusWidget widget, WidgetSurface surface)
        {
            _widget = widget ?? throw new ArgumentNullException(nameof(widget));
            _surface = surface;
            var strings = widget.Strings;
            Background = Brushes.White;

            _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.White, Visibility = Visibility.Hidden };

            _loading = new Border
            {
                Background = Brushes.White,
                Child = new TextBlock
                {
                    Text = strings.Loading, Foreground = Visuals.Muted, FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var retry = new Button { Content = strings.Retry, Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Center };
            retry.Click += (_, _) => Retry();
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 320 };
            panel.Children.Add(new TextBlock { Text = strings.OfflineTitle, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Visuals.Text, HorizontalAlignment = HorizontalAlignment.Center });
            panel.Children.Add(new TextBlock
            {
                Text = strings.OfflineBody, FontSize = 13, Foreground = Visuals.Muted, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 14),
            });
            panel.Children.Add(retry);
            _offline = new Border { Background = Brushes.White, Child = panel, Visibility = Visibility.Collapsed };

            var root = new Grid();
            root.Children.Add(_web);
            root.Children.Add(_loading);
            root.Children.Add(_offline);
            Content = root;

            // Se o ready não vier, mostra a página mesmo assim.
            _readyFallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _readyFallback.Tick += (_, _) => { _readyFallback.Stop(); ShowPage(); };
        }

        public WidgetSurface Surface => _surface;

        public bool HasUrl => _url != null;

        public void Navigate(string url)
        {
            _url = url;
            ShowLoading();
            _ = NavigateAsync(url);
        }

        public void ExecuteScript(string script) => _bridge?.ExecuteScript(script);

        public void MarkReady()
        {
            _readyFallback.Stop();
            ShowPage();
        }

        private async Task NavigateAsync(string url)
        {
            try
            {
                await EnsureCoreAsync();
                if (_url == url && !_disposed) WebView2Support.NavigateFresh(_web.CoreWebView2, url);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                Debug.WriteLine("[bfocus] WebView2: " + e);
                _init = null;
                ShowOffline();
            }
        }

        private Task EnsureCoreAsync() => _init ??= InitCoreAsync();

        private async Task InitCoreAsync()
        {
            var identity = _widget.Identity ?? throw new InvalidOperationException("widget sem init");
            var env = await WebView2Support.GetEnvironmentAsync(WebView2Support.UserDataFolder(identity.AppId));
            await _web.EnsureCoreWebView2Async(env);
            _bridge = new WebView2Bridge(_web.CoreWebView2, _widget, _surface, AskSavePath,
                onLoading: () => Ui(ShowLoading), onLoaded: () => Ui(OnLoaded), onFailed: () => Ui(ShowOffline));
        }

        private void ShowLoading()
        {
            _offline.Visibility = Visibility.Collapsed;
            _loading.Visibility = Visibility.Visible;
            _web.Visibility = Visibility.Hidden; // Hidden (não Collapsed): mantém o tamanho e a página carregando
        }

        private void ShowPage()
        {
            _loading.Visibility = Visibility.Collapsed;
            _offline.Visibility = Visibility.Collapsed;
            _web.Visibility = Visibility.Visible;
        }

        private void OnLoaded()
        {
            _readyFallback.Stop();
            _readyFallback.Start();
        }

        private void ShowOffline()
        {
            _readyFallback.Stop();
            _loading.Visibility = Visibility.Collapsed;
            _web.Visibility = Visibility.Hidden;
            _offline.Visibility = Visibility.Visible;
        }

        private void Retry()
        {
            if (_url == null) return;
            _widget.NotifySurfaceReloading(_surface);
            Navigate(_url);
        }

        private string? AskSavePath(string suggested)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = suggested, Title = _widget.Strings.SaveAsTitle, OverwritePrompt = true };
            return dlg.ShowDialog(Window.GetWindow(this)) == true ? dlg.FileName : null;
        }

        private void Ui(Action a)
        {
            if (_disposed) return;
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.BeginInvoke(a);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _readyFallback.Stop();
            _bridge?.Dispose();
            _web.Dispose();
        }
    }
}
