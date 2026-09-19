using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Bfocus.Widget.Tests.Support;
using Xunit;

namespace Bfocus.Widget.Tests
{
    /// <summary>
    /// Integração com o servidor simulado (§5.2 do BRIEF). Numa só classe: o mock tem estado global
    /// (cenário e log), então os testes daqui rodam em sequência.
    /// </summary>
    public class IntegrationTests : IClassFixture<MockServer>
    {
        private readonly MockServer _mock;

        public IntegrationTests(MockServer mock) { _mock = mock; }

        private BFocusConfig Config(Action<BFocusConfig>? tweak = null)
        {
            var c = Scenarios.ToConfig(Scenarios.Config("min"), _mock.BaseUrl, _mock.BaseUrl + "/v1");
            tweak?.Invoke(c);
            return c;
        }

        private static (BFocusWidget Widget, FakeHost Host, InMemoryStateStore Store) Create()
        {
            var host = new FakeHost();
            var store = new InMemoryStateStore();
            return (new BFocusWidget(host, new BFocusWidgetOptions { StateStore = store }), host, store);
        }

        [Fact]
        public async Task Default_launcher_state_real_com_os_headers_do_contrato()
        {
            _mock.SkipIfUnavailable();
            await _mock.ResetAsync("default");
            var (w, host, store) = Create();

            await w.InitAsync(Config(), TestContext.Current.CancellationToken);

            var log = await _mock.LogAsync();
            var req = log["requests"]!.AsArray().Single()!;
            Assert.Equal("POST", req["method"]!.GetValue<string>());
            Assert.Equal("/api/v1/widget/launcher-state", req["path"]!.GetValue<string>());
            Assert.Equal("{}", req["body"]!.GetValue<string>());
            var h = req["headers"]!;
            var id = w.Identity!;
            Assert.Equal("application/json", h["content-type"]!.GetValue<string>());
            Assert.Equal("bf_pk_test_123", h["x-bfocus-widget-key"]!.GetValue<string>());
            Assert.Equal(id.UserBase64, h["x-bfocus-widget-user"]!.GetValue<string>());
            Assert.Equal(Scenarios.Doc["userPayload"]![0]!["base64"]!.GetValue<string>(), h["x-bfocus-widget-user"]!.GetValue<string>());
            Assert.Equal("app://com.empresa.erp", h["x-bfocus-parent-origin"]!.GetValue<string>());
            Assert.Equal("dotnet/" + Protocol.PackageVersion, h["x-bfocus-client"]!.GetValue<string>());

            Assert.Equal("#0EA5E9", w.PrimaryColor);
            Assert.Equal("v4.2.0", w.ReleaseNotes.Label);
            Assert.Equal("2026-09-01T12:00:00+00:00", store.Get(id.LastSeenStorageKey));
            Assert.Empty(host.Shown);
            w.Dispose();
        }

        [Fact]
        public async Task Default_com_product_e_audience_na_query()
        {
            _mock.SkipIfUnavailable();
            await _mock.ResetAsync("default");
            var (w, _, _) = Create();
            await w.InitAsync(Config(c => { c.Product = "erp"; c.Audience = "both"; }), TestContext.Current.CancellationToken);
            var q = (await _mock.LogAsync())["requests"]![0]!["query"]!;
            Assert.Equal("erp", q["product"]!.GetValue<string>());
            Assert.Equal("both", q["audience"]!.GetValue<string>());
            w.Dispose();
        }

        [Fact]
        public async Task Banner_abre_o_release_notes_com_os_ids()
        {
            _mock.SkipIfUnavailable();
            await _mock.ResetAsync("banner");
            var (w, host, _) = Create();

            await w.InitAsync(Config(), TestContext.Current.CancellationToken);

            var shown = Assert.Single(host.Shown);
            Assert.Equal(WidgetSurface.ReleaseNotesBanner, shown.Surface);
            Assert.StartsWith(_mock.BaseUrl + "/v1/release-notes.html#host=native&hp=1&client=dotnet%2F" + Protocol.PackageVersion + "&", shown.Url);
            Assert.EndsWith("&view=banner&ids=11111111-1111-1111-1111-111111111111%2C22222222-2222-2222-2222-222222222222", shown.Url);
            Assert.Contains("&api=" + Rfc3986.Encode(_mock.BaseUrl), shown.Url);
            Assert.True(w.ReleaseNotes.Dot);

            // rn:done fecha e reconsulta (agora no default)
            await _mock.PostAsync("/__scenario", "{\"name\":\"default\"}");
            host.Deliver(WidgetSurface.ReleaseNotesBanner, "{\"type\":\"bfocus:rn:done\"}");
            await Wait.Until(() => !w.ReleaseNotes.Dot, what: "reconsulta depois do rn:done");
            Assert.Equal(2, (await _mock.LogAsync())["requests"]!.AsArray().Count);
            w.Dispose();
        }

        [Fact]
        public async Task Identity_error_chama_onError_e_repete_com_o_hash_novo()
        {
            _mock.SkipIfUnavailable();
            await _mock.ResetAsync("identity_error");
            var (w, _, _) = Create();
            var errors = new List<string>();
            w.Error += (_, e) => errors.Add(e.Code);
            var calls = 0;

            await w.InitAsync(Config(c => c.UserHashProvider = _ => Task.FromResult<string?>("hash-" + (++calls))), TestContext.Current.CancellationToken);

            Assert.Equal(new[] { Protocol.ErrorUserHashInvalid }, errors);
            Assert.Equal(2, calls);
            var reqs = (await _mock.LogAsync())["requests"]!.AsArray();
            Assert.Equal(2, reqs.Count);
            string Hash(JsonNode r) => JsonNode.Parse(Convert.FromBase64String(r["headers"]!["x-bfocus-widget-user"]!.GetValue<string>()))!["userHash"]!.GetValue<string>();
            Assert.Equal("hash-1", Hash(reqs[0]!));
            Assert.Equal("hash-2", Hash(reqs[1]!));
            Assert.Null(w.LastLauncherState);
            w.Dispose();
        }

        [Fact]
        public async Task Push_register_e_unregister_no_servidor()
        {
            _mock.SkipIfUnavailable();
            await _mock.ResetAsync("default");
            var (w, _, _) = Create();
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            await w.RegisterPushTokenAsync("fcm-token-1", "android", TestContext.Current.CancellationToken);
            await w.LogoutAsync(TestContext.Current.CancellationToken);

            var reqs = (await _mock.LogAsync())["requests"]!.AsArray();
            Assert.Equal(new[] { "/api/v1/widget/launcher-state", "/api/v1/widget/push/devices", "/api/v1/widget/push/devices/unregister" },
                reqs.Select(r => r!["path"]!.GetValue<string>()));
            var reg = Scenarios.Doc["push"]!["register"]!["expect"]!["json"]!;
            Assert.True(JsonNode.DeepEquals(reg, JsonNode.Parse(reqs[1]!["body"]!.GetValue<string>())));
            Assert.Equal("dotnet/" + Protocol.PackageVersion, reqs[1]!["headers"]!["x-bfocus-client"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{\"token\":\"fcm-token-1\"}"), JsonNode.Parse(reqs[2]!["body"]!.GetValue<string>())));
            w.Dispose();
        }

        [Fact]
        public async Task Embed_simulado_e_servido_na_origem_do_embed()
        {
            _mock.SkipIfUnavailable();
            await _mock.ResetAsync("default");
            var (w, _, _) = Create();
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            var url = EmbedUrl.Build(w.Identity!, extra: new[] { new KeyValuePair<string, string>("script", "all") });
            Assert.EndsWith("&script=all", url);
            Assert.True(w.Identity!.Origin.IsSameOrigin(url));
            var html = await _mock.Http.GetStringAsync(url.Substring(0, url.IndexOf('#')), TestContext.Current.CancellationToken);
            Assert.Contains("window.bFocusEmbed = { receive: receive, flush: flush, protocol: 1 }", html);
            Assert.Contains("w.chrome && w.chrome.webview", html); // o canal do WebView2 que os hosts expõem
            w.Dispose();
        }

        /// <summary>
        /// Canal pacote → embed: os scripts que o núcleo manda ao host (open, navigate, close) rodam num
        /// JS de verdade (node) contra um <c>window.bFocusEmbed.receive</c> que registra no mock, e o
        /// teste confere em <c>GET /__log</c> (<c>received</c>). Sem WebView no macOS, é o mais perto do
        /// WebView2.ExecuteScriptAsync que dá para rodar aqui.
        /// </summary>
        [Fact]
        public async Task Scripts_do_host_chegam_ao_receive_do_embed()
        {
            _mock.SkipIfUnavailable();
            await _mock.ResetAsync("default");
            var (w, host, _) = Create();
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);

            w.Open();
            await Wait.Until(() => host.Shown.Count == 1);
            host.Deliver(WidgetSurface.Tickets, "{\"type\":\"bfocus:ready\",\"payload\":{\"hp\":1,\"widget\":\"tickets\"}}");
            w.Open(WidgetTarget.Ticket("t-1"));
            await Wait.Until(() => host.Shown.Count == 2);
            w.Close();

            var scripts = host.Scripts.Where(s => s.Surface == WidgetSurface.Tickets).Select(s => s.Script).ToList();
            Assert.Equal(new[] { "bfocus:open", "bfocus:navigate", "bfocus:open", "bfocus:close" }, host.ScriptTypes(WidgetSurface.Tickets));

            var harness = Path.Combine(Path.GetTempPath(), "bfocus-harness-" + Guid.NewGuid().ToString("N") + ".mjs");
            File.WriteAllText(harness, """
                const [origin, file] = process.argv.slice(2)
                const scripts = JSON.parse((await import('node:fs')).readFileSync(file, 'utf8'))
                const pending = []
                globalThis.window = {
                  bFocusEmbed: {
                    protocol: 1,
                    flush() {},
                    receive(raw) {
                      const msg = typeof raw === 'string' ? JSON.parse(raw) : raw
                      if (!msg || typeof msg.type !== 'string' || !msg.type.startsWith('bfocus:')) return
                      pending.push(fetch(origin + '/__received', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(msg) }))
                    },
                  },
                }
                for (const s of scripts) (0, eval)(s)
                await Promise.all(pending)
                """);
            var list = harness + ".json";
            File.WriteAllText(list, new JsonArray(scripts.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()).ToJsonString());
            try
            {
                var psi = new ProcessStartInfo("node", $"\"{harness}\" {_mock.BaseUrl} \"{list}\"")
                {
                    RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
                };
                using var p = Process.Start(psi)!;
                await p.WaitForExitAsync(TestContext.Current.CancellationToken);
                Assert.True(p.ExitCode == 0, await p.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
            }
            finally
            {
                File.Delete(harness);
                File.Delete(list);
            }

            var received = (await _mock.LogAsync())["received"]!.AsArray();
            Assert.Equal(new[] { "bfocus:open", "bfocus:navigate", "bfocus:open", "bfocus:close" }, received.Select(r => r!["type"]!.GetValue<string>()));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{\"view\":\"ticket\",\"ticketId\":\"t-1\"}"), received[1]!["payload"]));
            w.Dispose();
        }
    }
}
