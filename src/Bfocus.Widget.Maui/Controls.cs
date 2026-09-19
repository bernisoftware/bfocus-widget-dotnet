using System;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Bfocus.Widget.Maui
{
    internal static class Visuals
    {
        public static Color Parse(string? hex)
        {
            ColorHex.ParseOrFallback(hex, out var r, out var g, out var b);
            return Color.FromRgb(r, g, b);
        }

        public static readonly Color Red = Parse(WidgetIcons.BadgeColor);

        private static Geometry Geo(string data) => (Geometry)new PathGeometryConverter().ConvertFromInvariantString(data)!;

        /// <summary>Ícone no viewBox 24×24, escalado para <paramref name="size"/>.</summary>
        public static View Icon(double size, params View[] parts)
        {
            var canvas = new Grid { WidthRequest = 24, HeightRequest = 24, Scale = size / 24, InputTransparent = true };
            foreach (var p in parts) canvas.Children.Add(p);
            return new Grid { WidthRequest = size, HeightRequest = size, InputTransparent = true, Children = { canvas }, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        }

        public static Path Stroke(string data, double thickness) => new Path
        {
            Data = Geo(data), Stroke = Colors.White, StrokeThickness = thickness, Aspect = Stretch.None,
            StrokeLineJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round,
        };

        public static Path ChatDots()
        {
            var g = new GeometryGroup();
            foreach (var x in WidgetIcons.ChatDotsX) g.Children.Add(new EllipseGeometry(new Point(x, 11), 1, 1));
            return new Path { Data = g, Fill = Colors.White, Aspect = Stretch.None };
        }

        public static Path Star() => new Path { Data = Geo(WidgetIcons.StarPath), Fill = Colors.White, Aspect = Stretch.None };

        public static void Ui(Action a)
        {
            if (MainThread.IsMainThread) a();
            else MainThread.BeginInvokeOnMainThread(a);
        }
    }

    /// <summary>
    /// Botão redondo de 56 px na cor do tenant, com o balão (✕ aberto) e o badge. Tocar abre/fecha
    /// o widget.
    /// </summary>
    public class BFocusLauncherButton : ContentView
    {
        private readonly Ellipse _circle;
        private readonly View _chat;
        private readonly View _close;
        private readonly Border _badge;
        private readonly Label _badgeText;
        private BFocusWidget? _widget;

        public BFocusLauncherButton()
        {
            WidthRequest = 64;
            HeightRequest = 64;
            _circle = new Ellipse
            {
                WidthRequest = 56, HeightRequest = 56, Fill = Visuals.Parse(Protocol.BrandFallbackColor),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.2f, Radius = 20, Offset = new Point(0, 6) },
            };
            _chat = Visuals.Icon(26, Visuals.Stroke(WidgetIcons.ChatBubblePath, 1.8), Visuals.ChatDots());
            _close = Visuals.Icon(26, Visuals.Stroke(WidgetIcons.ClosePath, 2));
            _close.IsVisible = false;
            _badgeText = new Label { FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            _badge = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 9 }, Stroke = Colors.White, StrokeThickness = 2,
                BackgroundColor = Visuals.Red, HeightRequest = 18, MinimumWidthRequest = 18, Padding = new Thickness(5, 0),
                HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Content = _badgeText,
                IsVisible = false, InputTransparent = true,
            };
            Content = new Grid { Children = { _circle, _chat, _close, _badge } };
            GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(Toggle) });
            SemanticProperties.SetDescription(this, WidgetStrings.For(null).LauncherName);
        }

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
                if (_widget == null) return;
                _widget.BadgeChanged += OnBadge;
                _widget.PrimaryColorChanged += OnColor;
                _widget.Opened += OnOpened;
                _widget.Closed += OnClosed;
                SetBadge(_widget.BadgeLabel);
                _circle.Fill = Visuals.Parse(_widget.PrimaryColor);
                SetOpen(_widget.IsOpen);
            }
        }

        private void SetBadge(string label)
        {
            _badgeText.Text = label;
            _badge.IsVisible = !string.IsNullOrEmpty(label);
        }

        private void SetOpen(bool open)
        {
            _chat.IsVisible = !open;
            _close.IsVisible = open;
            if (_widget != null) SemanticProperties.SetDescription(this, open ? _widget.Strings.LauncherCloseName : _widget.Strings.LauncherName);
        }

        private void OnBadge(object? sender, BadgeChangedEventArgs e) => Visuals.Ui(() => SetBadge(e.Label));
        private void OnColor(object? sender, PrimaryColorChangedEventArgs e) => Visuals.Ui(() => _circle.Fill = Visuals.Parse(e.Color));
        private void OnOpened(object? sender, EventArgs e) => Visuals.Ui(() => SetOpen(true));
        private void OnClosed(object? sender, EventArgs e) => Visuals.Ui(() => SetOpen(false));

        private void Toggle()
        {
            if (_widget == null || !_widget.IsInitialized) return;
            if (_widget.IsOpen) _widget.Close();
            else _widget.Open();
        }
    }

    /// <summary>Pílula de versão (<c>v4.2.0</c> / <c>—</c>) com estrela e ponto; tocar abre o histórico.</summary>
    public class BFocusReleaseBadge : ContentView
    {
        private readonly Border _pill;
        private readonly Label _label;
        private readonly Grid _dot;
        private readonly Ellipse _ring;
        private BFocusWidget? _widget;

        public BFocusReleaseBadge()
        {
            _label = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
            _ring = new Ellipse { WidthRequest = 12, HeightRequest = 12 };
            _dot = new Grid { VerticalOptions = LayoutOptions.Center, IsVisible = false, Children = { _ring, new Ellipse { WidthRequest = 8, HeightRequest = 8, Fill = Visuals.Red } } };
            _pill = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 999 }, StrokeThickness = 0, Padding = new Thickness(12, 6),
                HorizontalOptions = LayoutOptions.Start,
                Content = new HorizontalStackLayout { Spacing = 6, Children = { Visuals.Icon(14, Visuals.Star()), _label, _dot } },
            };
            Content = _pill;
            GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { if (_widget?.IsInitialized == true) _widget.OpenReleaseNotesHistory(); }) });
            SemanticProperties.SetDescription(this, WidgetStrings.For(null).ReleaseBadgeName);
            Apply(ReleaseNotesState.Empty);
        }

        public BFocusWidget? Widget
        {
            get => _widget;
            set
            {
                if (_widget != null) _widget.ReleaseNotesChanged -= OnChanged;
                _widget = value;
                if (_widget == null) return;
                _widget.ReleaseNotesChanged += OnChanged;
                Apply(_widget.ReleaseNotes);
            }
        }

        public void Apply(ReleaseNotesState state)
        {
            var color = Visuals.Parse(state.Color);
            _pill.BackgroundColor = color;
            _ring.Fill = color;
            _label.Text = state.Label;
            _dot.IsVisible = state.Dot;
            SemanticProperties.SetHint(this, state.Label);
        }

        private void OnChanged(object? sender, ReleaseNotesChangedEventArgs e) => Visuals.Ui(() => Apply(e.State));
    }
}
