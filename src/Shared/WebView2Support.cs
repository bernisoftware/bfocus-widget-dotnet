// Compilado (como link) em Bfocus.Widget.WinForms e Bfocus.Widget.Wpf: a parte do WebView2 que
// não depende do toolkit.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Bfocus.Widget.Hosting
{
    internal static class WebView2Support
    {
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, Task<CoreWebView2Environment>> Environments =
            new Dictionary<string, Task<CoreWebView2Environment>>(StringComparer.OrdinalIgnoreCase);
        private static bool? _available;
        private static readonly Lazy<HttpClient> DownloadClient = new Lazy<HttpClient>(() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

        /// <summary>
        /// <c>false</c> quando o runtime do WebView2 não está instalado: o widget cai no modo navegador.
        /// </summary>
        public static bool IsRuntimeAvailable()
        {
            lock (Lock)
            {
                if (_available.HasValue) return _available.Value;
                try
                {
                    _available = !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
                }
                catch (WebView2RuntimeNotFoundException)
                {
                    _available = false;
                }
                catch (Exception e) when (e is DllNotFoundException || e is BadImageFormatException || e is PlatformNotSupportedException)
                {
                    _available = false; // WebView2Loader.dll ausente / arquitetura errada
                }
                return _available.Value;
            }
        }

        /// <summary>
        /// Pasta de dados própria (<c>%LOCALAPPDATA%\bfocus\WebView2\&lt;appId&gt;</c>): o padrão do WebView2
        /// é ao lado do .exe, que em Program Files não tem permissão de escrita.
        /// </summary>
        public static string UserDataFolder(string appId) =>
            Path.Combine(FileStateStore.DefaultDirectory(), "WebView2", FileNames.Sanitize(appId.ToLowerInvariant(), "app"));

        /// <summary>Um ambiente por pasta, compartilhado entre as telas (um processo de navegador só).</summary>
        public static Task<CoreWebView2Environment> GetEnvironmentAsync(string userDataFolder)
        {
            lock (Lock)
            {
                if (!Environments.TryGetValue(userDataFolder, out var task) || task.IsFaulted || task.IsCanceled)
                {
                    task = CoreWebView2Environment.CreateAsync(null, userDataFolder);
                    Environments[userDataFolder] = task;
                }
                return task;
            }
        }

        /// <summary>
        /// Navega sempre com carga nova: se só o fragmento mudou (hash renovado, fila nova do banner),
        /// troca a query para o navegador não tratar como navegação no mesmo documento.
        /// </summary>
        public static void NavigateFresh(CoreWebView2 core, string url)
        {
            var target = EmbedUrl.IsSameDocument(core.Source, url) ? EmbedUrl.WithReloadToken(url, DateTime.UtcNow.Ticks) : url;
            core.Navigate(target);
        }

        /// <summary>Apaga localStorage, IndexedDB, cookies e cache só da origem do embed (logout).</summary>
        public static Task ClearOriginDataAsync(CoreWebView2 core, string origin) =>
            core.CallDevToolsProtocolMethodAsync(
                "Storage.clearDataForOrigin",
                "{\"origin\":" + JsonText.Quote(origin) + ",\"storageTypes\":\"all\"}");

        /// <summary>Baixa a URL pré-assinada do anexo direto para o arquivo escolhido.</summary>
        public static async Task DownloadToFileAsync(string url, string path, CancellationToken ct = default)
        {
            using (var response = await DownloadClient.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var tmp = path + ".part";
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await input.CopyToAsync(output, 81920, ct).ConfigureAwait(false);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
        }
    }
}
