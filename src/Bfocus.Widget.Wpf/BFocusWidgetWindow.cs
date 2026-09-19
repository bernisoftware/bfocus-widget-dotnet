using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Bfocus.Widget.Wpf
{
    /// <summary>
    /// Painel do widget (400 × 620, como no web): chamados ou histórico. Sem moldura do sistema
    /// (o embed tem cabeçalho e ✕); fechar só esconde, a WebView fica.
    /// </summary>
    public class BFocusWidgetWindow : Window
    {
        public const double PanelWidth = 400;
        public const double PanelHeight = 620;

        private readonly BFocusWidget _widget;
        private readonly WidgetSurface _surface;

        public BFocusWidgetWindow(BFocusWidget widget, WidgetSurface surface)
        {
            _widget = widget ?? throw new ArgumentNullException(nameof(widget));
            _surface = surface;
            var strings = widget.Strings;
            Title = surface == WidgetSurface.Tickets ? strings.PanelTitle : strings.ReleaseNotesTitle;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.White;
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            BorderThickness = new Thickness(1);
            Width = PanelWidth;
            Height = PanelHeight;
            Host = new BFocusWebViewHost(widget, surface);
            Content = Host;
        }

        public BFocusWebViewHost Host { get; }

        public bool AllowClose { get; set; }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!AllowClose)
            {
                e.Cancel = true;
                _widget.NotifySurfaceClosedByUser(_surface);
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            Host.Dispose();
            base.OnClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _widget.NotifySurfaceClosedByUser(_surface);
                return;
            }
            base.OnKeyDown(e);
        }

        /// <summary>Canto inferior direito da janela dona (ou da área de trabalho), acima do botão.</summary>
        public void PositionNear(Window? owner)
        {
            var area = OwnerArea(owner);
            var work = SystemParameters.WorkArea;
            Height = Math.Min(PanelHeight, Math.Max(320, area.Height - 100));
            Width = PanelWidth;
            var x = area.Right - 20 - Width;
            var y = area.Bottom - 88 - Height;
            Left = Math.Max(work.Left, Math.Min(x, work.Right - Width));
            Top = Math.Max(work.Top, Math.Min(y, work.Bottom - Height));
        }

        internal static Rect OwnerArea(Window? owner)
        {
            if (owner == null || !owner.IsLoaded || owner.WindowState == WindowState.Minimized) return SystemParameters.WorkArea;
            if (owner.WindowState == WindowState.Maximized) return SystemParameters.WorkArea;
            return new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight);
        }
    }

    /// <summary>
    /// Banner de ciência: modal cobrindo a janela dona, sem ✕, Esc ou Alt+F4. Só fecha no
    /// <c>bfocus:rn:done</c>.
    /// </summary>
    public sealed class BFocusBannerWindow : Window
    {
        public BFocusBannerWindow(BFocusWidget widget)
        {
            if (widget == null) throw new ArgumentNullException(nameof(widget));
            Title = widget.Strings.ReleaseNotesTitle;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.White;
            Host = new BFocusWebViewHost(widget, WidgetSurface.ReleaseNotesBanner);
            Content = Host;
        }

        public BFocusWebViewHost Host { get; }

        public bool AllowClose { get; set; }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!AllowClose) e.Cancel = true; // o usuário não fecha o banner
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            Host.Dispose();
            base.OnClosed(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape || (e.SystemKey == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))) e.Handled = true;
            base.OnPreviewKeyDown(e);
        }

        public void CoverOwner(Window? owner)
        {
            var area = BFocusWidgetWindow.OwnerArea(owner);
            Left = area.Left;
            Top = area.Top;
            Width = area.Width;
            Height = area.Height;
        }
    }
}
