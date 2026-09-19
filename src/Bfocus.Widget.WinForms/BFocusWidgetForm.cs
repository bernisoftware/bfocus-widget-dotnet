using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Bfocus.Widget.WinForms
{
    /// <summary>
    /// Painel do widget (400 × 620, como no web): chamados ou histórico de versões. Sem borda do
    /// sistema, porque o embed tem cabeçalho e ✕ próprios; fechar só esconde (a WebView fica).
    /// </summary>
    public class BFocusWidgetForm : Form
    {
        public const int PanelWidth = 400;
        public const int PanelHeight = 620;

        private readonly BFocusWidget _widget;
        private readonly WidgetSurface _surface;

        public BFocusWidgetForm(BFocusWidget widget, WidgetSurface surface)
        {
            _widget = widget ?? throw new ArgumentNullException(nameof(widget));
            _surface = surface;
            var strings = widget.Strings;
            Text = surface == WidgetSurface.Tickets ? strings.PanelTitle : strings.ReleaseNotesTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            KeyPreview = true;
            Size = new Size(PanelWidth, PanelHeight);
            Host = new BFocusWebViewHost(widget, surface) { Dock = DockStyle.Fill };
            Controls.Add(Host);
        }

        public BFocusWebViewHost Host { get; }

        /// <summary>Liberado pelo host no descarte; o usuário só esconde.</summary>
        public bool AllowClose { get; set; }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW: sombra como a do painel web
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.TryRoundCorners(Handle);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _widget.NotifySurfaceClosedByUser(_surface);
            }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                _widget.NotifySurfaceClosedByUser(_surface);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Canto inferior direito da janela dona (ou da tela), acima do botão flutuante.</summary>
        public void PositionNear(Form? owner)
        {
            var scale = DeviceDpi / 96f;
            var area = owner != null && !owner.IsDisposed && owner.WindowState != FormWindowState.Minimized
                ? owner.RectangleToScreen(owner.ClientRectangle)
                : Screen.FromPoint(Cursor.Position).WorkingArea;
            var screen = Screen.FromRectangle(area).WorkingArea;
            var w = (int)(PanelWidth * scale);
            // Como o web: altura máxima = área − 100 px.
            var h = Math.Min((int)(PanelHeight * scale), Math.Max((int)(320 * scale), area.Height - (int)(100 * scale)));
            var x = area.Right - (int)(20 * scale) - w;
            var y = area.Bottom - (int)(88 * scale) - h;
            x = Math.Max(screen.Left, Math.Min(x, screen.Right - w));
            y = Math.Max(screen.Top, Math.Min(y, screen.Bottom - h));
            Bounds = new Rectangle(x, y, w, h);
        }
    }

    /// <summary>
    /// Banner de ciência das release notes: modal cobrindo a janela dona, sem ✕, sem Esc e sem
    /// Alt+F4. Só fecha quando o embed manda <c>bfocus:rn:done</c>.
    /// </summary>
    public sealed class BFocusBannerForm : Form
    {
        public BFocusBannerForm(BFocusWidget widget)
        {
            if (widget == null) throw new ArgumentNullException(nameof(widget));
            Text = widget.Strings.ReleaseNotesTitle;
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            KeyPreview = true;
            Host = new BFocusWebViewHost(widget, WidgetSurface.ReleaseNotesBanner) { Dock = DockStyle.Fill };
            Controls.Add(Host);
        }

        public BFocusWebViewHost Host { get; }

        public bool AllowClose { get; set; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // O usuário não fecha o banner; o app saindo (ou a dona fechando) pode.
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape || keyData == (Keys.Alt | Keys.F4)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Tela cheia sobre a janela dona (ou a tela toda, sem dona).</summary>
        public void CoverOwner(Form? owner)
        {
            Bounds = owner != null && !owner.IsDisposed && owner.WindowState != FormWindowState.Minimized
                ? owner.RectangleToScreen(owner.ClientRectangle)
                : Screen.FromPoint(Cursor.Position).WorkingArea;
        }
    }

    internal static class Native
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Cantos arredondados no Windows 11 (no 10 não faz nada).</summary>
        public static void TryRoundCorners(IntPtr hwnd)
        {
            try
            {
                var round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
            }
        }
    }
}
