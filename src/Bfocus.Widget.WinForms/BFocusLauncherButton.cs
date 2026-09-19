using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Bfocus.Widget.WinForms
{
    /// <summary>
    /// Botão redondo de 56 px na cor do tenant, com o balão (✕ quando aberto) e o badge
    /// (<c>•</c>, <c>N</c>, <c>99+</c>). Clicar abre/fecha o widget. O controle tem 64 px para o badge
    /// caber fora do círculo, como no web (top/right −4 px).
    /// </summary>
    public class BFocusLauncherButton : Control
    {
        private BFocusWidget? _widget;
        private string _badge = string.Empty;
        private Color _primary = Drawing.ParseColor(Protocol.BrandFallbackColor);
        private bool _open;
        private bool _hover;

        public BFocusLauncherButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Size = new Size(64, 64);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = WidgetStrings.For(null).LauncherName;
        }

        /// <summary>Widget ligado ao botão (badge, cor, abrir/fechar).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public BFocusWidget? Widget
        {
            get => _widget;
            set
            {
                if (_widget != null)
                {
                    _widget.BadgeChanged -= OnBadge;
                    _widget.PrimaryColorChanged -= OnColor;
                    _widget.Opened -= OnOpened;
                    _widget.Closed -= OnClosed;
                }
                _widget = value;
                if (_widget != null)
                {
                    _widget.BadgeChanged += OnBadge;
                    _widget.PrimaryColorChanged += OnColor;
                    _widget.Opened += OnOpened;
                    _widget.Closed += OnClosed;
                    _badge = _widget.BadgeLabel;
                    _primary = Drawing.ParseColor(_widget.PrimaryColor);
                    _open = _widget.IsOpen;
                    AccessibleName = _open ? _widget.Strings.LauncherCloseName : _widget.Strings.LauncherName;
                }
                Invalidate();
            }
        }

        /// <summary>Label do badge (preenchido pelo widget; pode ser usado sem ele).</summary>
        [DefaultValue("")]
        public string BadgeLabel
        {
            get => _badge;
            set { _badge = value ?? string.Empty; Invalidate(); }
        }

        /// <summary>Cor do círculo (padrão: índigo bFocus até o tenant mandar a sua).</summary>
        public Color PrimaryColor
        {
            get => _primary;
            set { _primary = value; Invalidate(); }
        }

        private void OnBadge(object? sender, BadgeChangedEventArgs e) => Ui(() => BadgeLabel = e.Label);
        private void OnColor(object? sender, PrimaryColorChangedEventArgs e) => Ui(() => PrimaryColor = Drawing.ParseColor(e.Color));
        private void OnOpened(object? sender, EventArgs e) => Ui(() => SetOpen(true));
        private void OnClosed(object? sender, EventArgs e) => Ui(() => SetOpen(false));

        private void SetOpen(bool open)
        {
            _open = open;
            if (_widget != null) AccessibleName = open ? _widget.Strings.LauncherCloseName : _widget.Strings.LauncherName;
            Invalidate();
        }

        private void Ui(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (_widget == null || !_widget.IsInitialized) return;
            if (_widget.IsOpen) _widget.Close();
            else _widget.Open();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var s = Math.Min(Width, Height) / 64f; // escala (DPI ou tamanho escolhido)
            var inset = 4 * s;
            var circle = new RectangleF(inset, inset - (_hover ? 1 * s : 0), 56 * s, 56 * s);

            using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                g.FillEllipse(shadow, circle.X, circle.Y + 2 * s, circle.Width, circle.Height);
            using (var fill = new SolidBrush(_primary))
                g.FillEllipse(fill, circle);

            var icon = new RectangleF(circle.X + (circle.Width - 26 * s) / 2, circle.Y + (circle.Height - 26 * s) / 2, 26 * s, 26 * s);
            if (_open) Drawing.DrawCloseIcon(g, icon, Color.White);
            else Drawing.DrawChatIcon(g, icon, Color.White);

            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Color.FromArgb(160, _primary), 2 * s)) g.DrawEllipse(pen, circle.X - 2 * s, circle.Y - 2 * s, circle.Width + 4 * s, circle.Height + 4 * s);

            if (_badge.Length == 0) return;
            using (var font = new Font("Segoe UI", 11f * s, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var text = g.MeasureString(_badge, font);
                var h = 18 * s;
                var w = Math.Max(h, text.Width + 10 * s);
                var rect = new RectangleF(Width - w, 0, w, h);
                using (var path = Drawing.RoundedRect(rect, h / 2))
                using (var red = new SolidBrush(Drawing.BadgeRed))
                using (var border = new Pen(Color.White, 2 * s))
                using (var white = new SolidBrush(Color.White))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.FillPath(red, path);
                    g.DrawPath(border, path);
                    g.DrawString(_badge, font, white, rect, fmt);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Widget = null;
            base.Dispose(disposing);
        }
    }
}
