using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Bfocus.Widget.Hosting;
using Microsoft.Web.WebView2.Wpf;
using WinForms = System.Windows.Forms;

namespace Bfocus.Widget.Wpf
{
    /// <summary>
    /// Host WPF do <see cref="BFocusWidget"/>: janelas com WebView2, banner modal, "salvar como",
    /// notificação na bandeja e modo navegador quando falta o runtime do WebView2.
    /// </summary>
    /// <example>
    /// <code>
    /// var widget = BFocusWpfHost.CreateWidget(this);   // no construtor da MainWindow
    /// await widget.InitAsync(new BFocusConfig { ... });
    /// launcher.Widget = widget;                         // &lt;bf:BFocusLauncherButton x:Name="launcher"/&gt;
    /// </code>
    /// </example>
    public sealed class BFocusWpfHost : IBFocusWidgetHost, IDisposable
    {
        private readonly Dictionary<WidgetSurface, Window> _windows = new Dictionary<WidgetSurface, Window>();
        private BFocusWidget? _widget;
        private Window? _owner;
        private WinForms.NotifyIcon? _tray;
        private Action? _trayClick;

        public BFocusWpfHost(Window? owner = null)
        {
            Owner = owner;
        }

        /// <summary>Cria o widget já com este host (chame na thread de UI).</summary>
        public static BFocusWidget CreateWidget(Window? owner = null, BFocusWidgetOptions? options = null) =>
            new BFocusWidget(new BFocusWpfHost(owner), options);

        public Window? Owner
        {
            get => _owner;
            set
            {
                if (_owner != null)
                {
                    _owner.LocationChanged -= OnOwnerMoved;
                    _owner.SizeChanged -= OnOwnerMoved;
                }
                _owner = value;
                if (_owner != null)
                {
                    _owner.LocationChanged += OnOwnerMoved;
                    _owner.SizeChanged += OnOwnerMoved;
                }
            }
        }

        /// <summary>Ícone da notificação na bandeja. Padrão: o ícone do executável.</summary>
        public System.Drawing.Icon? NotificationIcon { get; set; }

        /// <summary>Força (<c>false</c>) o modo navegador, ou ignora a checagem do runtime (<c>true</c>).</summary>
        public bool? WebViewAvailableOverride { get; set; }

        public void Attach(BFocusWidget widget)
        {
            if (_widget != null && !ReferenceEquals(_widget, widget))
                throw new InvalidOperationException("Um BFocusWpfHost atende um só BFocusWidget.");
            _widget = widget;
        }

        public bool IsWebViewAvailable => WebViewAvailableOverride ?? WebView2Support.IsRuntimeAvailable();

        private BFocusWidget Widget => _widget ?? throw new InvalidOperationException("Host sem widget.");

        public void ShowSurface(WidgetSurface surface, string url, bool forceLoad)
        {
            if (surface == WidgetSurface.ReleaseNotesBanner)
            {
                ShowBanner(url);
                return;
            }
            if (!_windows.TryGetValue(surface, out var w))
            {
                w = new BFocusWidgetWindow(Widget, surface);
                if (Owner != null && Owner.IsLoaded) w.Owner = Owner;
                _windows[surface] = w;
            }
            var panel = (BFocusWidgetWindow)w;
            if (forceLoad || !panel.Host.HasUrl) panel.Host.Navigate(url);
            panel.PositionNear(Owner);
            if (!panel.IsVisible) panel.Show();
            panel.Activate();
        }

        private void ShowBanner(string url)
        {
            if (_windows.TryGetValue(WidgetSurface.ReleaseNotesBanner, out var existing) && existing.IsVisible)
            {
                ((BFocusBannerWindow)existing).Host.Navigate(url);
                return;
            }
            var banner = new BFocusBannerWindow(Widget);
            if (Owner != null && Owner.IsLoaded) banner.Owner = Owner;
            _windows[WidgetSurface.ReleaseNotesBanner] = banner;
            banner.Host.Navigate(url);
            banner.CoverOwner(Owner);
            // ShowDialog prende o fluxo até fechar: agenda para depois dos efeitos atuais.
            banner.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (_windows.TryGetValue(WidgetSurface.ReleaseNotesBanner, out var current) && ReferenceEquals(current, banner))
                    banner.ShowDialog();
            }));
        }

        public void HideSurface(WidgetSurface surface)
        {
            if (surface == WidgetSurface.ReleaseNotesBanner)
            {
                DestroySurface(surface);
                return;
            }
            if (_windows.TryGetValue(surface, out var w)) w.Hide();
            Owner?.Activate();
        }

        public void DestroySurface(WidgetSurface surface)
        {
            if (!_windows.TryGetValue(surface, out var w)) return;
            _windows.Remove(surface);
            if (w is BFocusWidgetWindow panel) panel.AllowClose = true;
            if (w is BFocusBannerWindow banner) banner.AllowClose = true;
            w.Close();
        }

        public void ExecuteScript(WidgetSurface surface, string script) => HostOf(surface)?.ExecuteScript(script);

        public void MarkReady(WidgetSurface surface) => HostOf(surface)?.MarkReady();

        public void OpenExternal(string url) => SystemBrowser.Open(url);

        public void Download(string url, string fileName)
        {
            var strings = Widget.Strings;
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = fileName, Title = strings.SaveAsTitle, OverwritePrompt = true };
            var owner = ActiveWindow();
            var ok = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            if (ok != true) return;
            _ = DownloadAsync(url, dlg.FileName, strings);
        }

        private async Task DownloadAsync(string url, string path, WidgetStrings strings)
        {
            try
            {
                await WebView2Support.DownloadToFileAsync(url, path);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                Debug.WriteLine("[bfocus] download: " + e);
                MessageBox.Show(strings.DownloadFailed, strings.PanelTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void ShowNotification(string title, string body, Action onClick)
        {
            if (_tray == null)
            {
                _tray = new WinForms.NotifyIcon();
                _tray.BalloonTipClicked += (_, _) => { _trayClick?.Invoke(); HideTray(); };
                _tray.Click += (_, _) => { _trayClick?.Invoke(); HideTray(); };
                _tray.BalloonTipClosed += (_, _) => HideTray();
            }
            _trayClick = onClick;
            _tray.Icon = NotificationIcon ?? AppIcon() ?? System.Drawing.SystemIcons.Information;
            _tray.Text = title.Length > 63 ? title.Substring(0, 63) : title;
            _tray.Visible = true;
            _tray.ShowBalloonTip(5000, title, body, WinForms.ToolTipIcon.Info);
        }

        private static System.Drawing.Icon? AppIcon()
        {
            try
            {
                var exe = Environment.ProcessPath;
                return exe == null ? null : System.Drawing.Icon.ExtractAssociatedIcon(exe);
            }
            catch (Exception e) when (e is ArgumentException || e is System.IO.IOException)
            {
                return null;
            }
        }

        private void HideTray()
        {
            if (_tray != null) _tray.Visible = false;
        }

        public void ClearWebViewData(WidgetIdentity identity)
        {
            if (!IsWebViewAvailable) return;
            _ = ClearAsync(identity);
        }

        /// <summary>As telas já foram descartadas no logout: usa uma WebView2 oculta e temporária.</summary>
        private static async Task ClearAsync(WidgetIdentity identity)
        {
            var web = new WebView2();
            var window = new Window
            {
                Width = 1, Height = 1, Left = -32000, Top = -32000, ShowInTaskbar = false,
                WindowStyle = WindowStyle.None, ShowActivated = false, Content = web,
            };
            window.Show();
            try
            {
                var env = await WebView2Support.GetEnvironmentAsync(WebView2Support.UserDataFolder(identity.AppId));
                await web.EnsureCoreWebView2Async(env);
                await WebView2Support.ClearOriginDataAsync(web.CoreWebView2, identity.Origin.Value);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                Debug.WriteLine("[bfocus] limpar dados da WebView: " + e);
            }
            finally
            {
                web.Dispose();
                window.Close();
            }
        }

        private BFocusWebViewHost? HostOf(WidgetSurface surface)
        {
            if (!_windows.TryGetValue(surface, out var w)) return null;
            return w is BFocusWidgetWindow p ? p.Host : w is BFocusBannerWindow b ? b.Host : null;
        }

        private Window? ActiveWindow()
        {
            foreach (var w in _windows.Values)
                if (w.IsVisible && w.IsActive) return w;
            return Owner;
        }

        private void OnOwnerMoved(object? sender, EventArgs e)
        {
            foreach (var w in _windows.Values)
            {
                if (!w.IsVisible) continue;
                if (w is BFocusWidgetWindow p) p.PositionNear(Owner);
                else if (w is BFocusBannerWindow b) b.CoverOwner(Owner);
            }
        }

        public void Dispose()
        {
            foreach (var s in new List<WidgetSurface>(_windows.Keys)) DestroySurface(s);
            Owner = null;
            _tray?.Dispose();
            _tray = null;
        }
    }
}
