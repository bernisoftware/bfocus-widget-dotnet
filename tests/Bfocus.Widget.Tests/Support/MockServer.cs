using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Bfocus.Widget.Tests.Support
{
    /// <summary>
    /// Sobe <c>node widgets-native/conformance/mock-server.mjs 0</c> (porta livre) e lê a porta na
    /// primeira linha. Sem node ou fora do monorepo (espelho público), os testes de integração pulam.
    /// </summary>
    public sealed class MockServer : IAsyncLifetime
    {
        private Process? _process;

        public string? SkipReason { get; private set; }
        public string BaseUrl { get; private set; } = string.Empty;
        public HttpClient Http { get; } = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public static string? FindScript()
        {
            var env = Environment.GetEnvironmentVariable("BFOCUS_MOCK_SERVER");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "widgets-native", "conformance", "mock-server.mjs");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        public async ValueTask InitializeAsync()
        {
            var script = FindScript();
            if (script == null)
            {
                SkipReason = "mock-server.mjs não encontrado (fora do monorepo; defina BFOCUS_MOCK_SERVER)";
                return;
            }
            var psi = new ProcessStartInfo("node", "\"" + script + "\" 0")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            try
            {
                _process = Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                SkipReason = "node não está no PATH";
                return;
            }
            if (_process == null) { SkipReason = "node não iniciou"; return; }

            var lineTask = _process.StandardOutput.ReadLineAsync();
            var done = await Task.WhenAny(lineTask, Task.Delay(TimeSpan.FromSeconds(15)));
            var line = done == lineTask ? await lineTask : null;
            var m = line == null ? null : Regex.Match(line, @"http://127\.0\.0\.1:(\d+)");
            if (m == null || !m.Success)
            {
                SkipReason = "mock-server não informou a porta: " + line;
                Kill();
                return;
            }
            BaseUrl = "http://127.0.0.1:" + m.Groups[1].Value;
        }

        public void SkipIfUnavailable()
        {
            if (SkipReason != null) Assert.Skip(SkipReason);
        }

        public async Task ResetAsync(string scenario = "default")
        {
            await PostAsync("/__reset", "");
            await PostAsync("/__scenario", "{\"name\":\"" + scenario + "\"}");
        }

        public async Task<JsonNode> LogAsync() =>
            JsonNode.Parse(await Http.GetStringAsync(BaseUrl + "/__log"))!;

        public Task PostAsync(string path, string json) =>
            Http.PostAsync(BaseUrl + path, new StringContent(json, Encoding.UTF8, "application/json"));

        public ValueTask DisposeAsync()
        {
            Kill();
            Http.Dispose();
            return default;
        }

        private void Kill()
        {
            // Só o processo que este teste subiu.
            try
            {
                if (_process != null && !_process.HasExited) _process.Kill();
            }
            catch (InvalidOperationException) { }
            _process?.Dispose();
            _process = null;
        }
    }
}
