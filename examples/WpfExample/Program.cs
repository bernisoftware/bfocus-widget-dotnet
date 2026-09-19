using System;
using System.Windows;
using System.Windows.Controls;
using Bfocus.Widget;
using Bfocus.Widget.Wpf;

namespace WpfExample
{
    /// <summary>Exemplo mínimo em WPF, só com código (sem XAML). Variáveis iguais às do exemplo WinForms.</summary>
    public static class Program
    {
        [STAThread]
        public static void Main() => new Application().Run(new MainWindow());
    }

    public sealed class MainWindow : Window
    {
        private readonly BFocusWidget _widget;

        public MainWindow()
        {
            Title = "ERP de exemplo (WPF)";
            Width = 1000;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _widget = BFocusWpfHost.CreateWidget(this);
            _widget.Error += (_, e) => Title = $"ERP de exemplo — erro do widget: {e.Code}";

            var root = new Grid();
            root.Children.Add(new BFocusReleaseBadge { Widget = _widget, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
            root.Children.Add(new BFocusLauncherButton { Widget = _widget, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
            Content = root;

            Loaded += async (_, _) => await _widget.InitAsync(new BFocusConfig
            {
                PublishableKey = Environment.GetEnvironmentVariable("BFOCUS_KEY") ?? "bf_pk_test_123",
                AppId = "com.empresa.erp",
                User = new BFocusUser { ExternalId = "USR-1", Name = "Ana Souza" },
                Customer = new BFocusCustomer { ExternalId = "ACME-1", Name = "Acme Ltda" },
                UserHash = Environment.GetEnvironmentVariable("BFOCUS_USER_HASH"), // calculado no seu servidor
                ApiBaseUrl = Environment.GetEnvironmentVariable("BFOCUS_API") ?? Protocol.DefaultApiBaseUrl,
                EmbedBaseUrl = Environment.GetEnvironmentVariable("BFOCUS_EMBED") ?? Protocol.DefaultEmbedBaseUrl,
                Notifications = true,
            });
            Closed += (_, _) => _widget.Dispose();
        }
    }
}
