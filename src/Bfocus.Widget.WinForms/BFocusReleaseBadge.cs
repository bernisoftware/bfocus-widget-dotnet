using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Bfocus.Widget.WinForms
{
    /// <summary>
    /// Pílula de versão (<c>v4.2.0</c> ou <c>—</c>) na cor do tenant, com a estrela e o ponto de
    /// novidade não vista. Clicar abre o histórico de versões.
    /// </summary>
    public class BFocusReleaseBadge : Control
    {
        private BFocusWidget? _widget;
        private ReleaseNotesState _state = ReleaseNotesState.Empty;

        public BFocusReleaseBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = WidgetStrings.For(null).ReleaseBadgeName;
            Font = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
            UpdateSize();
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public BFocusWidget? Widget
        {
            get => _widget;
            set
            {
                if (_widget != null) _widget.ReleaseNotesChanged -= OnChanged;
                _widget = value;
                if (_widget != null)
                {
                    _widget.ReleaseNotesChanged += OnChanged;
                    State = _widget.ReleaseNotes;
                    AccessibleName = _widget.Strings.ReleaseBadgeName;
                }
            }
        }

        /// <summary>Estado mostrado (preenchido pelo widget).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ReleaseNotesState State
        {
            get => _state;
            set
            {
                _state = value ?? ReleaseNotesState.Empty;
                AccessibleDescription = _state.Label;
                UpdateSize();
                Invalidate();
            }
        }

        private void OnChanged(object? sender, ReleaseNotesChangedEventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(new Action(() => State = e.State));
            else State = e.State;
        }

        private float DpiScale => DeviceDpi / 96f;

        private void UpdateSize()
        {
            var s = DpiScale;
            using (var g = CreateGraphics())
            {
                var text = g.MeasureString(_state.Label, Font);
                var w = 12 * s + 14 * s + 6 * s + text.Width + (_state.Dot ? 6 * s + 8 * s : 0) + 12 * s;
                var h = Math.Max(14 * s, text.Height) + 12 * s;
                Size = new Size((int)Math.Ceiling(w), (int)Math.Ceiling(h));
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateSize();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (_widget != null && _widget.IsInitialized) _widget.OpenReleaseNotesHistory();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var s = DpiScale;
            var primary = Drawing.ParseColor(_state.Color);
            var rect = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Drawing.RoundedRect(rect, rect.Height / 2))
            using (var fill = new SolidBrush(primary))
                g.FillPath(fill, path);

            var x = 12 * s;
            var cy = Height / 2f;
            Drawing.FillStar(g, new RectangleF(x, cy - 7 * s, 14 * s, 14 * s), Color.White);
            x += 14 * s + 6 * s;
            var text = g.MeasureString(_state.Label, Font);
            using (var white = new SolidBrush(Color.White))
                g.DrawString(_state.Label, Font, white, x, cy - text.Height / 2);
            x += text.Width;

            if (!_state.Dot) return;
            x += 6 * s;
            using (var ring = new SolidBrush(primary))
            using (var red = new SolidBrush(Drawing.BadgeRed))
            {
                g.FillEllipse(ring, x - 2 * s, cy - 6 * s, 12 * s, 12 * s);
                g.FillEllipse(red, x, cy - 4 * s, 8 * s, 8 * s);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Widget = null;
            base.Dispose(disposing);
        }
    }
}
