using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Bfocus.Widget.Wpf
{
    /// <summary>Cores e ícones do widget web em WPF.</summary>
    internal static class Visuals
    {
        public static Color ParseColor(string? hex)
        {
            ColorHex.ParseOrFallback(hex, out var r, out var g, out var b);
            return Color.FromRgb(r, g, b);
        }

        public static SolidColorBrush Brush(string? hex)
        {
            var b = new SolidColorBrush(ParseColor(hex));
            b.Freeze();
            return b;
        }

        public static readonly SolidColorBrush Red = Brush(WidgetIcons.BadgeColor);
        public static readonly SolidColorBrush Muted = Brush("#64748B");
        public static readonly SolidColorBrush Text = Brush("#0F172A");

        /// <summary>Ícone 24×24 dentro de um Viewbox do tamanho pedido.</summary>
        public static Viewbox Icon(double size, params UIElement[] parts)
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            foreach (var p in parts) canvas.Children.Add(p);
            return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
        }

        public static Path Stroke(string data, double thickness) => new Path
        {
            Data = Geometry.Parse(data),
            Stroke = Brushes.White,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        public static Path ChatDots()
        {
            var group = new GeometryGroup();
            foreach (var x in WidgetIcons.ChatDotsX) group.Children.Add(new EllipseGeometry(new Point(x, 11), 1, 1));
            return new Path { Data = group, Fill = Brushes.White };
        }

        public static Path Star() => new Path { Data = Geometry.Parse(WidgetIcons.StarPath), Fill = Brushes.White };

        /// <summary>Template mínimo: só o conteúdo (o visual é todo nosso).</summary>
        public static ControlTemplate BareTemplate()
        {
            var t = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            t.Seal();
            return t;
        }
    }
}
