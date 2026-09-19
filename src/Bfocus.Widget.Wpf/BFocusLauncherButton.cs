using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Bfocus.Widget.Wpf
{
    /// <summary>
    /// Botão redondo de 56 px na cor do tenant, com o balão (✕ quando aberto) e o badge
    /// (<c>•</c>, <c>N</c>, <c>99+</c>). Clicar abre/fecha o widget. Ocupa 64 px para o badge caber
    /// fora do círculo, como no web.
    /// </summary>
    public class BFocusLauncherButton : Button
    {
        private readonly Ellipse _circle;
        private readonly UIElement _chatIcon;
        private readonly UIElement _closeIcon;
        private readonly Border _badge;
        private readonly TextBlock _badgeText;
        private BFocusWidget? _widget;

        public BFocusLauncherButton()
        {
            Width = 64;
            Height = 64;
            Cursor = Cursors.Hand;
            Template = Visuals.BareTemplate();
            FocusVisualStyle = null;

            _circle = new Ellipse
            {
                Width = 56, Height = 56, Margin = new Thickness(4),
                Fill = Visuals.Brush(Protocol.BrandFallbackColor),
                Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 6, Direction = 270, Opacity = 0.2 },
            };
            _chatIcon = Visuals.Icon(26, Visuals.Stroke(WidgetIcons.ChatBubblePath, 1.8), Visuals.ChatDots());
            _closeIcon = Visuals.Icon(26, Visuals.Stroke(WidgetIcons.ClosePath, 2));
            _closeIcon.Visibility = Visibility.Collapsed;
            _badgeText = new TextBlock
            {
                FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            _badge = new Border
            {
                Height = 18, MinWidth = 18, CornerRadius = new CornerRadius(9), Padding = new Thickness(5, 0, 5, 0),
                Background = Visuals.Red, BorderBrush = Brushes.White, BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Child = _badgeText, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            };

            var grid = new Grid { Width = 64, Height = 64, Background = Brushes.Transparent };
            grid.Children.Add(_circle);
            grid.Children.Add(new Grid { Width = 56, Height = 56, Children = { _chatIcon, _closeIcon } });
            grid.Children.Add(_badge);
            Content = grid;
            AutomationProperties_SetName(WidgetStrings.For(null).LauncherName);
        }

        /// <summary>Widget ligado ao botão (badge, cor, abrir/fechar).</summary>
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
                SetColor(_widget.PrimaryColor);
                SetOpen(_widget.IsOpen);
            }
        }

        public void SetBadge(string label)
        {
            _badgeText.Text = label ?? string.Empty;
            _badge.Visibility = string.IsNullOrEmpty(label) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void SetColor(string hex) => _circle.Fill = Visuals.Brush(hex);

        private void SetOpen(bool open)
        {
            _chatIcon.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            _closeIcon.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (_widget != null) AutomationProperties_SetName(open ? _widget.Strings.LauncherCloseName : _widget.Strings.LauncherName);
        }

        private void AutomationProperties_SetName(string name) =>
            System.Windows.Automation.AutomationProperties.SetName(this, name);

        private void OnBadge(object? sender, BadgeChangedEventArgs e) => Ui(() => SetBadge(e.Label));
        private void OnColor(object? sender, PrimaryColorChangedEventArgs e) => Ui(() => SetColor(e.Color));
        private void OnOpened(object? sender, EventArgs e) => Ui(() => SetOpen(true));
        private void OnClosed(object? sender, EventArgs e) => Ui(() => SetOpen(false));

        private void Ui(Action a)
        {
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.BeginInvoke(a);
        }

        protected override void OnClick()
        {
            base.OnClick();
            if (_widget == null || !_widget.IsInitialized) return;
            if (_widget.IsOpen) _widget.Close();
            else _widget.Open();
        }
    }
}
