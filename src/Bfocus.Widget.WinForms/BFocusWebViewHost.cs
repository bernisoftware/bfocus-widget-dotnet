using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bfocus.Widget.Hosting;
using Microsoft.Web.WebView2.WinForms;

namespace Bfocus.Widget.WinForms
{
    /// <summary>
    /// Uma tela do widget: WebView2 com o embed, "carregando" até o <c>bfocus:ready</c> e a tela
    /// "sem conexão" com "tentar de novo". Pode ser usado direto num layout próprio; o
    /// <see cref="BFocusWinFormsHost"/> o coloca nos painéis padrão.
    /// </summary>
    public sealed class BFocusWebViewHost : UserControl
    {
        private readonly BFocusWidget _widget;
        private readonly WidgetSurface _surface;
        private readonly WebView2 _web;
        private readonly Panel _loading;
        private readonly Panel _offline;
        private readonly Timer _readyFallback;
        private WebView2Bridge? _bridge;
        private Task? _init;
        private string? _url;

        public BFocusWebViewHost(BFocusWidget widget, WidgetSurface surface)
        {
            _widget = widget ?? throw new ArgumentNullException(nameof(widget));
            _surface = surface;
            var strings = widget.Strings;
            BackColor = Color.White;

            _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };

            _loading = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _loading.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = strings.Loading,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(0x64, 0x74, 0x8B),
                Font = new Font("Segoe UI", 10f),
            });

            _offline = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Visible = false };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            layout.Controls.Add(new Label
            {
                Text = strings.OfflineTitle, AutoSize = true, Anchor = AnchorStyles.None,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(0x0F, 0x17, 0x2A),
            }, 0, 1);
            layout.Controls.Add(new Label
            {
                Text = strings.OfflineBody, AutoSize = true, Anchor = AnchorStyles.None, MaximumSize = new Size(320, 0),
                Font = new Font("Segoe UI", 9.5f), ForeColor = Color.FromArgb(0x64, 0x74, 0x8B), Padding = new Padding(0, 6, 0, 12),
                TextAlign = ContentAlignment.MiddleCenter,
            }, 0, 2);
            var retry = new Button { Text = strings.Retry, AutoSize = true, Anchor = AnchorStyles.None, Padding = new Padding(12, 4, 12, 4) };
            retry.Click += (_, _) => Retry();
            layout.Controls.Add(retry, 0, 3);
            _offline.Controls.Add(layout);

            Controls.Add(_web);
            Controls.Add(_loading);
            Controls.Add(_offline);
            _loading.BringToFront();

            // Se o ready não vier (embed antigo, erro de script), mostra a página mesmo assim.
            _readyFallback = new Timer { Interval = 8000 };
            _readyFallback.Tick += (_, _) => { _readyFallback.Stop(); _loading.Visible = false; };
        }

        public WidgetSurface Surface => _surface;

        /// <summary>Já recebeu uma URL (a WebView é mantida entre aberturas).</summary>
        public bool HasUrl => _url != null;

        /// <summary>Navega para a URL do embed (primeiro carregamento, recarga, identidade nova).</summary>
        public void Navigate(string url)
        {
            _url = url;
            ShowLoading();
            _ = NavigateAsync(url);
        }

        public void ExecuteScript(string script) => _bridge?.ExecuteScript(script);

        /// <summary><c>bfocus:ready</c>: some o "carregando".</summary>
        public void MarkReady()
        {
            _readyFallback.Stop();
            _loading.Visible = false;
            _offline.Visible = false;
        }

        private async Task NavigateAsync(string url)
        {
            try
            {
                await EnsureCoreAsync();
                if (_url == url) WebView2Support.NavigateFresh(_web.CoreWebView2, url);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                Debug.WriteLine("[bfocus] WebView2: " + e);
                _init = null; // tenta criar de novo no "tentar de novo"
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
                onLoading: () => UiPost(ShowLoading), onLoaded: () => UiPost(OnLoaded), onFailed: () => UiPost(ShowOffline));
        }

        private void ShowLoading()
        {
            _offline.Visible = false;
            _loading.Visible = true;
            _loading.BringToFront();
        }

        private void OnLoaded()
        {
            _readyFallback.Stop();
            _readyFallback.Start();
        }

        private void ShowOffline()
        {
            _readyFallback.Stop();
            _loading.Visible = false;
            _offline.Visible = true;
            _offline.BringToFront();
        }

        private void Retry()
        {
            if (_url == null) return;
            _widget.NotifySurfaceReloading(_surface);
            Navigate(_url);
        }

        private string? AskSavePath(string suggested)
        {
            using (var dlg = new SaveFileDialog { FileName = suggested, Title = _widget.Strings.SaveAsTitle, OverwritePrompt = true })
                return dlg.ShowDialog(FindForm()) == DialogResult.OK ? dlg.FileName : null;
        }

        private void UiPost(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bridge?.Dispose();
                _readyFallback.Dispose();
                _web.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
