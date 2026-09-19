using System;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bfocus.Widget;
using Bfocus.Widget.WinForms;

namespace WinFormsExample
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    /// <summary>
    /// Exemplo mínimo. Para testar com o servidor simulado:
    ///   node widgets-native/conformance/mock-server.mjs 8787
    ///   set BFOCUS_API=http://127.0.0.1:8787 &amp; set BFOCUS_EMBED=http://127.0.0.1:8787/v1
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly BFocusWidget _widget;

        public MainForm()
        {
            Text = "ERP de exemplo";
            ClientSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;

            _widget = BFocusWinFormsHost.CreateWidget(this);
            _widget.Error += (_, e) => Text = $"ERP de exemplo — erro do widget: {e.Code}";

            var launcher = new BFocusLauncherButton { Widget = _widget, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            launcher.Location = new Point(ClientSize.Width - launcher.Width - 16, ClientSize.Height - launcher.Height - 16);
            var pill = new BFocusReleaseBadge { Widget = _widget, Location = new Point(16, 16) };
            Controls.Add(launcher);
            Controls.Add(pill);

            Load += async (_, _) => await InitAsync();
            FormClosed += (_, _) => _widget.Dispose();
        }

        private async Task InitAsync()
        {
            await _widget.InitAsync(new BFocusConfig
            {
                PublishableKey = Environment.GetEnvironmentVariable("BFOCUS_KEY") ?? "bf_pk_test_123",
                AppId = "com.empresa.erp",
                User = new BFocusUser { ExternalId = "USR-1", Name = "Ana Souza", Email = "ana@empresa.com.br" },
                Customer = new BFocusCustomer { ExternalId = "ACME-1", Name = "Acme Ltda" },
                // O hash vem do SEU servidor (sign_widget_identity dos SDKs). Nunca o segredo no app.
                UserHashProvider = FetchUserHashAsync,
                ApiBaseUrl = Environment.GetEnvironmentVariable("BFOCUS_API") ?? Protocol.DefaultApiBaseUrl,
                EmbedBaseUrl = Environment.GetEnvironmentVariable("BFOCUS_EMBED") ?? Protocol.DefaultEmbedBaseUrl,
                Notifications = true,
            });
        }

        private static async Task<string?> FetchUserHashAsync(System.Threading.CancellationToken ct)
        {
            var endpoint = Environment.GetEnvironmentVariable("BFOCUS_HASH_URL");
            if (string.IsNullOrEmpty(endpoint)) return null; // sem servidor de exemplo: segue sem hash
            using var http = new HttpClient();
            return (await http.GetStringAsync(endpoint, ct)).Trim();
        }
    }
}
