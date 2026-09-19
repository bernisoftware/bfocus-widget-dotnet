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
    /// Pílula de versão (<c>v4.2.0</c> ou <c>—</c>) na cor do tenant, com estrela e ponto de novidade.
    /// Clicar abre o histórico de versões.
    /// </summary>
    public class BFocusReleaseBadge : Button
    {
        private readonly Border _pill;
        private readonly TextBlock _label;
        private readonly Grid _dot;
        private readonly Ellipse _dotRing;
        private BFocusWidget? _widget;

        public BFocusReleaseBadge()
        {
            Cursor = Cursors.Hand;
            Template = Visuals.BareTemplate();
            FocusVisualStyle = null;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Center;

            _label = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _dotRing = new Ellipse { Width = 12, Height = 12 };
            _dot = new Grid { Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            _dot.Children.Add(_dotRing);
            _dot.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Visuals.Red });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Visuals.Icon(14, Visuals.Star()));
            row.Children.Add(_label);
            row.Children.Add(_dot);
            _pill = new Border
            {
                CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 6, 12, 6), Child = row,
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Direction = 270, Opacity = 0.16 },
            };
            Content = _pill;
            Apply(ReleaseNotesState.Empty);
            System.Windows.Automation.AutomationProperties.SetName(this, WidgetStrings.For(null).ReleaseBadgeName);
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
                System.Windows.Automation.AutomationProperties.SetName(this, _widget.Strings.ReleaseBadgeName);
            }
        }

        /// <summary>Mostra um estado (preenchido pelo widget).</summary>
        public void Apply(ReleaseNotesState state)
        {
            var brush = Visuals.Brush(state.Color);
            _pill.Background = brush;
            _dotRing.Fill = brush;
            _label.Text = state.Label;
            _dot.Visibility = state.Dot ? Visibility.Visible : Visibility.Collapsed;
            System.Windows.Automation.AutomationProperties.SetHelpText(this, state.Label);
        }

        private void OnChanged(object? sender, ReleaseNotesChangedEventArgs e)
        {
            if (Dispatcher.CheckAccess()) Apply(e.State);
            else Dispatcher.BeginInvoke(new Action(() => Apply(e.State)));
        }

        protected override void OnClick()
        {
            base.OnClick();
            if (_widget != null && _widget.IsInitialized) _widget.OpenReleaseNotesHistory();
        }
    }
}
