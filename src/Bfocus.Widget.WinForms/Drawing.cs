using System.Drawing;
using System.Drawing.Drawing2D;

namespace Bfocus.Widget.WinForms
{
    /// <summary>Desenho dos ícones do widget web (viewBox 24×24) em GDI+.</summary>
    internal static class Drawing
    {
        public static Color ParseColor(string? hex)
        {
            ColorHex.ParseOrFallback(hex, out var r, out var g, out var b);
            return Color.FromArgb(r, g, b);
        }

        public static Color BadgeRed => ParseColor(WidgetIcons.BadgeColor);

        public static void DrawChatIcon(Graphics g, RectangleF box, Color color)
        {
            var k = box.Width / 24f;
            PointF P(float x, float y) => new PointF(box.X + x * k, box.Y + y * k);
            using (var path = new GraphicsPath())
            using (var pen = new Pen(color, 1.8f * k) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var fill = new SolidBrush(color))
            {
                // M3 11c0-4.4 4-8 9-8s9 3.6 9 8c0 4.4-4 8-9 8-1 0-2-.1-2.9-.4L4 21l1.4-4.4C4 15.2 3 13.2 3 11z
                path.AddBezier(P(3, 11), P(3, 6.6f), P(7, 3), P(12, 3));
                path.AddBezier(P(12, 3), P(17, 3), P(21, 6.6f), P(21, 11));
                path.AddBezier(P(21, 11), P(21, 15.4f), P(17, 19), P(12, 19));
                path.AddBezier(P(12, 19), P(11, 19), P(10, 18.9f), P(9.1f, 18.6f));
                path.AddLine(P(9.1f, 18.6f), P(4, 21));
                path.AddLine(P(4, 21), P(5.4f, 16.6f));
                path.AddBezier(P(5.4f, 16.6f), P(4, 15.2f), P(3, 13.2f), P(3, 11));
                path.CloseFigure();
                g.DrawPath(pen, path);
                foreach (var x in WidgetIcons.ChatDotsX)
                {
                    var c = P(x, 11);
                    g.FillEllipse(fill, c.X - k, c.Y - k, 2 * k, 2 * k);
                }
            }
        }

        public static void DrawCloseIcon(Graphics g, RectangleF box, Color color)
        {
            var k = box.Width / 24f;
            using (var pen = new Pen(color, 2f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, box.X + 6 * k, box.Y + 6 * k, box.X + 18 * k, box.Y + 18 * k);
                g.DrawLine(pen, box.X + 6 * k, box.Y + 18 * k, box.X + 18 * k, box.Y + 6 * k);
            }
        }

        public static void FillStar(Graphics g, RectangleF box, Color color)
        {
            var k = box.Width / 24f;
            var pts = new PointF[WidgetIcons.StarPolygon.Length / 2];
            for (var i = 0; i < pts.Length; i++)
                pts[i] = new PointF(box.X + WidgetIcons.StarPolygon[2 * i] * k, box.Y + WidgetIcons.StarPolygon[2 * i + 1] * k);
            using (var b = new SolidBrush(color)) g.FillPolygon(b, pts);
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var d = System.Math.Min(radius * 2, System.Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
