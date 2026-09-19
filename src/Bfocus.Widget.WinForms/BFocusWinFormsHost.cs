using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bfocus.Widget.Hosting;
using Microsoft.Web.WebView2.WinForms;

namespace Bfocus.Widget.WinForms
{
    /// <summary>
    /// Host Windows Forms do <see cref="BFocusWidget"/>: painéis com WebView2, banner modal, "salvar
    /// como", notificação na bandeja e modo navegador quando falta o runtime do WebView2.
    /// </summary>
    /// <example>
    /// <code>
    /// var widget = BFocusWinFormsHost.CreateWidget(this);   // no construtor do Form principal
    /// await widget.InitAsync(new BFocusConfig { ... });
    /// Controls.Add(new BFocusLauncherButton { Widget = widget, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, ... });
    /// </code>
    /// </example>
    public sealed class BFocusWinFormsHost : IBFocusWidgetHost, IDisposable
    {
        private readonly Dictionary<WidgetSurface, Form> _forms = new Dictionary<WidgetSurface, Form>();
        private BFocusWidget? _widget;
        private Form? _owner;
        private NotifyIcon? _tray;
        private Action? _trayClick;

        public BFocusWinFormsHost(Form? owner = null)
        {
            Owner = owner;
        }

        /// <summary>Cria o widget já com este host (chame na thread de UI).</summary>
        public static BFocusWidget CreateWidget(Form? owner = null, BFocusWidgetOptions? options = null) =>
            new BFocusWidget(new BFocusWinFormsHost(owner), options);

        /// <summary>Janela principal: o painel se posiciona sobre ela e o banner a cobre.</summary>
        public Form? Owner
        {
            get => _owner;
            set
            {
                if (_owner != null)
                {
                    _owner.Move -= OnOwnerMoved;
                    _owner.Resize -= OnOwnerMoved;
                }
                _owner = value;
                if (_owner != null)
                {
                    _owner.Move += OnOwnerMoved;
                    _owner.Resize += OnOwnerMoved;
                }
            }
        }

        /// <summary>Ícone da notificação na bandeja. Padrão: o ícone da janela dona.</summary>
        public Icon? NotificationIcon { get; set; }

        /// <summary>Força (<c>false</c>) o modo navegador, ou ignora a checagem do runtime (<c>true</c>).</summary>
        public bool? WebViewAvailableOverride { get; set; }

        public void Attach(BFocusWidget widget)
        {
            if (_widget != null && !ReferenceEquals(_widget, widget))
                throw new InvalidOperationException("Um BFocusWinFormsHost atende um só BFocusWidget.");
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
            if (!_forms.TryGetValue(surface, out var f) || f.IsDisposed)
            {
                f = new BFocusWidgetForm(Widget, surface);
                _forms[surface] = f;
            }
            var panel = (BFocusWidgetForm)f;
            if (forceLoad || !panel.Host.HasUrl) panel.Host.Navigate(url);
            panel.PositionNear(Owner);
            if (!panel.Visible)
            {
                if (Owner != null && !Owner.IsDisposed) panel.Show(Owner);
                else panel.Show();
            }
            panel.Activate();
        }

        private void ShowBanner(string url)
        {
            if (_forms.TryGetValue(WidgetSurface.ReleaseNotesBanner, out var existing) && !existing.IsDisposed && existing.Visible)
            {
                ((BFocusBannerForm)existing).Host.Navigate(url); // fila nova com o banner já aberto
                return;
            }
            var banner = new BFocusBannerForm(Widget);
            _forms[WidgetSurface.ReleaseNotesBanner] = banner;
            banner.Host.Navigate(url);
            banner.CoverOwner(Owner);
            var owner = Owner;
            void ShowModal()
            {
                if (banner.IsDisposed) return;
                if (owner != null && !owner.IsDisposed) banner.ShowDialog(owner);
                else banner.ShowDialog();
            }
            // ShowDialog prende a thread até fechar: sai do fluxo atual para não travar os próximos efeitos.
            var ctx = SynchronizationContext.Current;
            if (ctx != null) ctx.Post(_ => ShowModal(), null);
            else ShowModal();
        }

        public void HideSurface(WidgetSurface surface)
        {
            if (surface == WidgetSurface.ReleaseNotesBanner)
            {
                DestroySurface(surface);
                return;
            }
            if (_forms.TryGetValue(surface, out var f) && !f.IsDisposed) f.Hide();
            Owner?.Activate();
        }

        public void DestroySurface(WidgetSurface surface)
        {
            if (!_forms.TryGetValue(surface, out var f)) return;
            _forms.Remove(surface);
            if (f.IsDisposed) return;
            if (f is BFocusWidgetForm panel) panel.AllowClose = true;
            if (f is BFocusBannerForm banner) banner.AllowClose = true;
            f.Close();
            f.Dispose();
        }

        public void ExecuteScript(WidgetSurface surface, string script) => HostOf(surface)?.ExecuteScript(script);

        public void MarkReady(WidgetSurface surface) => HostOf(surface)?.MarkReady();

        public void OpenExternal(string url) => SystemBrowser.Open(url);

        public void Download(string url, string fileName)
        {
            var strings = Widget.Strings;
            string path;
            using (var dlg = new SaveFileDialog { FileName = fileName, Title = strings.SaveAsTitle, OverwritePrompt = true })
            {
                if (dlg.ShowDialog(ActiveWindow()) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            _ = DownloadAsync(url, path, strings);
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
                MessageBox.Show(ActiveWindow(), strings.DownloadFailed, strings.PanelTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void ShowNotification(string title, string body, Action onClick)
        {
            if (_tray == null)
            {
                _tray = new NotifyIcon();
                _tray.BalloonTipClicked += (_, _) => { _trayClick?.Invoke(); HideTray(); };
                _tray.Click += (_, _) => { _trayClick?.Invoke(); HideTray(); };
                _tray.BalloonTipClosed += (_, _) => HideTray();
            }
            _trayClick = onClick;
            _tray.Icon = NotificationIcon ?? Owner?.Icon ?? SystemIcons.Information;
            _tray.Text = title.Length > 63 ? title.Substring(0, 63) : title;
            _tray.Visible = true;
            _tray.ShowBalloonTip(5000, title, body, ToolTipIcon.Info);
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
            using (var form = new Form
            {
                ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000), Size = new Size(1, 1), Opacity = 0,
            })
            using (var web = new WebView2 { Dock = DockStyle.Fill })
            {
                form.Controls.Add(web);
                form.Show();
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
                    form.Close();
                }
            }
        }

        private BFocusWebViewHost? HostOf(WidgetSurface surface)
        {
            if (!_forms.TryGetValue(surface, out var f) || f.IsDisposed) return null;
            return f is BFocusWidgetForm p ? p.Host : f is BFocusBannerForm b ? b.Host : null;
        }

        private IWin32Window? ActiveWindow()
        {
            foreach (var f in _forms.Values)
                if (!f.IsDisposed && f.Visible && f.ContainsFocus) return f;
            return Owner;
        }

        private void OnOwnerMoved(object? sender, EventArgs e)
        {
            foreach (var f in _forms.Values)
            {
                if (f.IsDisposed || !f.Visible) continue;
                if (f is BFocusWidgetForm p) p.PositionNear(Owner);
                else if (f is BFocusBannerForm b) b.CoverOwner(Owner);
            }
        }

        public void Dispose()
        {
            foreach (var s in new List<WidgetSurface>(_forms.Keys)) DestroySurface(s);
            Owner = null;
            _tray?.Dispose();
            _tray = null;
        }
    }
}
