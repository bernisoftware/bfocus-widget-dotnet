using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Bfocus.Widget.Tests.Support;
using Xunit;

namespace Bfocus.Widget.Tests
{
    /// <summary>O <see cref="BFocusWidget"/> inteiro com host e HTTP de mentira.</summary>
    public class WidgetFlowTests
    {
        private const string EmbedBase = "https://widget.bfocus.com.br/v1";

        // Sem o escape padrão (que troca "+" por + e quebraria os Replace dos testes).
        private static string Fixture(string name) => Scenarios.Doc["launcherStateFixtures"]![name]!.ToJsonString(
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        private static (BFocusWidget Widget, FakeHost Host, StubHandler Http, InMemoryStateStore Store) Create(
            Func<CapturedRequest, int, (int, string)>? respond = null, bool webView = true, InMemoryStateStore? store = null)
        {
            var http = new StubHandler(respond ?? ((_, _) => (200, StubHandler.Envelope(Fixture("default")))));
            var host = new FakeHost { WebViewAvailable = webView };
            store ??= new InMemoryStateStore();
            var widget = new BFocusWidget(host, new BFocusWidgetOptions { HttpClient = new HttpClient(http), StateStore = store });
            return (widget, host, http, store);
        }

        private static BFocusConfig Config(Action<BFocusConfig>? tweak = null)
        {
            var c = Scenarios.ToConfig(Scenarios.Config("min"));
            tweak?.Invoke(c);
            return c;
        }

        [Fact]
        public async Task Init_faz_a_primeira_consulta_com_o_client_dotnet_e_grava_a_base()
        {
            var (w, host, http, store) = Create();
            var badges = new List<string>();
            var colors = new List<string>();
            var pills = new List<ReleaseNotesState>();
            w.BadgeChanged += (_, e) => badges.Add(e.Label);
            w.PrimaryColorChanged += (_, e) => colors.Add(e.Color);
            w.ReleaseNotesChanged += (_, e) => pills.Add(e.State);

            await w.InitAsync(Config(), TestContext.Current.CancellationToken);

            var r = Assert.Single(http.Requests);
            Assert.Equal("dotnet/" + Protocol.PackageVersion, r.Headers["X-bFocus-Client"]);
            Assert.Equal("app://com.empresa.erp", r.Headers["X-bFocus-Parent-Origin"]);
            Assert.Empty(badges); // primeira visita: não acende
            Assert.Equal("2026-09-01T12:00:00+00:00", store.Get(w.Identity!.LastSeenStorageKey));
            Assert.Equal(new[] { "#0EA5E9" }, colors);
            var pill = Assert.Single(pills);
            Assert.Equal(("v4.2.0", false, "#0EA5E9"), (pill.Label, pill.Dot, pill.Color));
            Assert.Empty(host.Shown); // sem banner
            w.Dispose();
        }

        [Fact]
        public async Task Evento_novo_acende_o_ponto_e_notifica_so_com_notifications()
        {
            var calls = 0;
            var (w, host, _, store) = Create((_, i) =>
            {
                calls++;
                var latest = i == 0 ? "2026-09-01T12:00:00+00:00" : "2026-09-01T12:30:00+00:00";
                return (200, StubHandler.Envelope(Fixture("default").Replace("2026-09-01T12:00:00+00:00", latest)));
            });
            var badges = new List<string>();
            w.BadgeChanged += (_, e) => badges.Add(e.Label);

            await w.InitAsync(Config(c => c.Notifications = true), TestContext.Current.CancellationToken);
            Assert.Empty(badges);
            await w.RefreshAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new[] { "•" }, badges);
            var n = Assert.Single(host.Notifications);
            Assert.Equal(("Suporte", "Há novidades nos seus chamados."), (n.Title, n.Body));
            // base não se move com o state: só o bfocus:seen move
            Assert.Equal("2026-09-01T12:00:00+00:00", store.Get(w.Identity!.LastSeenStorageKey));

            // clicar na notificação abre o widget
            n.OnClick();
            await Wait.Until(() => host.Shown.Any(s => s.Surface == WidgetSurface.Tickets), what: "abrir pela notificação");
            w.Dispose();
        }

        [Fact]
        public async Task Sem_notifications_nao_notifica()
        {
            var (w, host, _, store) = Create((_, i) =>
                (200, StubHandler.Envelope(Fixture("default").Replace("2026-09-01T12:00:00+00:00", i == 0 ? "2026-09-01T12:00:00+00:00" : "2026-09-01T13:00:00+00:00"))));
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            await w.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal("•", w.BadgeLabel);
            Assert.Empty(host.Notifications);
            w.Dispose();
        }

        [Fact]
        public async Task Open_carrega_o_embed_e_o_ciclo_do_mock_embed_script()
        {
            var (w, host, _, store) = Create();
            var opened = 0; var closed = 0;
            var errors = new List<string>();
            var badges = new List<string>();
            var colors = new List<string>();
            w.Opened += (_, _) => opened++;
            w.Closed += (_, _) => closed++;
            w.Error += (_, e) => errors.Add(e.Code);
            w.BadgeChanged += (_, e) => badges.Add(e.Label);
            w.PrimaryColorChanged += (_, e) => colors.Add(e.Color);
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);

            w.Open();
            await Wait.Until(() => host.Shown.Count == 1, what: "ShowSurface");
            var shown = host.Shown[0];
            Assert.Equal(WidgetSurface.Tickets, shown.Surface);
            Assert.True(shown.ForceLoad);
            Assert.Equal(EmbedUrl.Build(w.Identity!), shown.Url);
            Assert.Equal(1, opened);
            Assert.True(w.IsOpen);

            // o embed simulado manda a sequência de scenarios.mockEmbedScript.tickets
            var origin = w.Identity!.Origin.Value;
            foreach (var m in Scenarios.Doc["mockEmbedScript"]!["tickets"]!.AsArray())
                host.Deliver(WidgetSurface.Tickets, m!.ToJsonString().Replace("{origin}", origin));

            Assert.Contains("ready:Tickets", host.Calls);
            Assert.Equal(new[] { "bfocus:open", "bfocus:close" }, host.ScriptTypes(WidgetSurface.Tickets)); // open no ready; close no fim
            Assert.Contains("#123456", colors);
            Assert.Equal("3", badges.First());
            Assert.Equal("2026-09-01T10:05:00+00:00", store.Get(w.Identity!.LastSeenStorageKey));
            Assert.Equal(new[] { "https://example.com/docs" }, host.External);
            Assert.Equal(new[] { (origin + "/files/manual.pdf", "manual.pdf") }, host.Downloads);
            Assert.Equal(new[] { Protocol.ErrorUserHashInvalid }, errors);
            Assert.Equal(1, closed);
            Assert.False(w.IsOpen);
            Assert.Contains("hide:Tickets", host.Calls);
            w.Dispose();
        }

        [Fact]
        public async Task Reabrir_mantem_a_WebView_e_deep_link_vira_navigate()
        {
            var (w, host, _, _) = Create();
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);

            w.Open(WidgetTarget.Ticket("abc-123"));
            await Wait.Until(() => host.Shown.Count == 1);
            Assert.EndsWith("&open=ticket%3Aabc-123", host.Shown[0].Url);
            host.Deliver(WidgetSurface.Tickets, "{\"type\":\"bfocus:ready\",\"payload\":{\"hp\":1,\"widget\":\"tickets\"}}");
            w.Close();

            w.Open(WidgetTarget.Chat("c-1"));
            await Wait.Until(() => host.Shown.Count == 2);
            Assert.False(host.Shown[1].ForceLoad); // mesma WebView
            var scripts = host.Scripts.Where(s => s.Surface == WidgetSurface.Tickets).Select(s => s.Script).ToList();
            Assert.Contains(HostScript.Receive(MessageTypes.Navigate, "{\"view\":\"chat\",\"conversationId\":\"c-1\"}"), scripts);
            Assert.Equal(new[] { "bfocus:open", "bfocus:close", "bfocus:navigate", "bfocus:open" }, host.ScriptTypes(WidgetSurface.Tickets));
            w.Dispose();
        }

        [Fact]
        public async Task Navigate_antes_do_ready_espera_o_ready()
        {
            var (w, host, _, _) = Create();
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            w.Open();
            await Wait.Until(() => host.Shown.Count == 1);
            w.Open(WidgetTarget.New); // página ainda carregando
            await Wait.Until(() => host.Shown.Count == 2);
            Assert.Empty(host.ScriptTypes(WidgetSurface.Tickets));
            host.Deliver(WidgetSurface.Tickets, "{\"type\":\"bfocus:ready\",\"payload\":{\"hp\":1}}");
            Assert.Equal(new[] { "bfocus:open", "bfocus:navigate" }, host.ScriptTypes(WidgetSurface.Tickets));
            w.Dispose();
        }

        [Fact]
        public async Task Aberto_o_state_nao_mexe_no_badge_e_fechar_consulta_na_hora()
        {
            var (w, host, http, _) = Create();
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            w.Open();
            await Wait.Until(() => w.IsOpen);
            host.Deliver(WidgetSurface.Tickets, "{\"type\":\"bfocus:unread\",\"payload\":{\"count\":120}}");
            Assert.Equal("99+", w.BadgeLabel);
            await w.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal("99+", w.BadgeLabel);

            var before = http.Requests.Count;
            host.Deliver(WidgetSurface.Tickets, "{\"type\":\"bfocus:close\"}");
            await Wait.Until(() => http.Requests.Count == before + 1, what: "consulta ao fechar");
            w.Dispose();
        }

        [Fact]
        public async Task Mensagens_de_outra_origem_ou_desconhecidas_sao_ignoradas()
        {
            var (w, host, _, _) = Create();
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            w.HandleEmbedMessage(WidgetSurface.Tickets, "https://evil.example/v1/embed.html", "{\"type\":\"bfocus:unread\",\"payload\":{\"count\":5}}");
            Assert.Equal("", w.BadgeLabel);
            host.Deliver(WidgetSurface.Tickets, "{\"type\":\"bfocus:futuro\",\"payload\":{}}");
            host.Deliver(WidgetSurface.Tickets, "{\"type\":\"bfocus:openExternal\",\"payload\":{\"url\":\"javascript:alert(1)\"}}");
            host.Deliver(WidgetSurface.Tickets, "{\"type\":\"bfocus:download\",\"payload\":{\"url\":\"file:///etc/passwd\",\"filename\":\"x\"}}");
            Assert.Empty(host.External);
            Assert.Empty(host.Downloads);
            w.Dispose();
        }

        [Fact]
        public async Task Banner_abre_sozinho_nao_reabre_a_mesma_fila_e_fecha_no_done()
        {
            var bannerCalls = 0;
            var (w, host, http, _) = Create((_, _) =>
            {
                bannerCalls++;
                return (200, StubHandler.Envelope(bannerCalls <= 2 ? Fixture("banner") : Fixture("default")));
            });
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);

            var banner = Assert.Single(host.Shown);
            Assert.Equal(WidgetSurface.ReleaseNotesBanner, banner.Surface);
            var ids = Scenarios.Doc["launcherStateFixtures"]!["banner"]!["release_notes"]!["banner_ids"]!.AsArray().Select(x => x!.GetValue<string>());
            Assert.Equal(EmbedUrl.Build(w.Identity!, EmbedPage.ReleaseNotes, view: "banner", ids: ids), banner.Url);
            Assert.True(w.ReleaseNotes.Dot);

            await w.RefreshAsync(TestContext.Current.CancellationToken); // mesma fila: não reabre
            Assert.Single(host.Shown);

            foreach (var m in Scenarios.Doc["mockEmbedScript"]!["release-notes:banner"]!.AsArray())
                host.Deliver(WidgetSurface.ReleaseNotesBanner, m!.ToJsonString());

            Assert.Equal("#123456", w.ReleaseNotes.Color); // rn:branding
            Assert.Contains("destroy:ReleaseNotesBanner", host.Calls);
            await Wait.Until(() => http.Requests.Count == 3, what: "reconsulta depois do rn:done");
            await Wait.Until(() => !w.ReleaseNotes.Dot);
            Assert.Single(host.Shown);
            w.Dispose();
        }

        [Fact]
        public async Task Banner_desligado_nao_abre()
        {
            var (w, host, _, _) = Create((_, _) => (200, StubHandler.Envelope(Fixture("banner"))));
            await w.InitAsync(Config(c => c.AutoShowReleaseBanner = false), TestContext.Current.CancellationToken);
            Assert.Empty(host.Shown);
            Assert.Equal(2, w.ReleaseNotes.BannerIds.Count);
            w.Dispose();
        }

        [Fact]
        public async Task Historico_abre_mantem_e_fecha()
        {
            var (w, host, _, _) = Create();
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            w.OpenReleaseNotesHistory();
            Assert.Equal(EmbedUrl.Build(w.Identity!, EmbedPage.ReleaseNotes, view: "history"), host.Shown.Single().Url);
            foreach (var m in Scenarios.Doc["mockEmbedScript"]!["release-notes:history"]!.AsArray())
                host.Deliver(WidgetSurface.ReleaseNotesHistory, m!.ToJsonString());
            Assert.Contains("hide:ReleaseNotesHistory", host.Calls);
            w.OpenReleaseNotesHistory();
            Assert.False(host.Shown[1].ForceLoad);
            w.Dispose();
        }

        [Fact]
        public async Task Modo_navegador_sem_WebView()
        {
            var (w, host, _, _) = Create(webView: false);
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            Assert.True(w.IsBrowserMode);
            w.Open();
            w.OpenReleaseNotesHistory();
            Assert.Empty(host.Shown);
            var id = w.Identity!;
            Assert.Equal(new[]
            {
                EmbedUrl.Build(id, EmbedPage.Tickets, HostMode.Browser),
                EmbedUrl.Build(id, EmbedPage.ReleaseNotes, HostMode.Browser, view: "history"),
            }, host.External);
            Assert.StartsWith("https://widget.bfocus.com.br/v1/embed.html#host=browser&hp=1&client=dotnet%2F" + Protocol.PackageVersion + "&", host.External[0]);
            w.Dispose();
        }

        [Fact]
        public async Task Hash_renovado_tambem_recusado_avisa_uma_vez_por_codigo()
        {
            var providerCalls = 0;
            var (w, _, http, _) = Create((_, _) => (401, "{\"code\":\"ERROR\",\"data\":null,\"message\":\"WIDGET_USER_HASH_INVALID\"}"));
            var errors = new List<string>();
            w.Error += (_, e) => errors.Add(e.Code);

            await w.InitAsync(Config(c => c.UserHashProvider = _ => Task.FromResult<string?>("h" + (++providerCalls))),
                TestContext.Current.CancellationToken);
            Assert.Equal(2, providerCalls);
            Assert.Equal(2, http.Requests.Count);
            Assert.Equal(new[] { Protocol.ErrorUserHashInvalid }, errors);

            await w.RefreshAsync(TestContext.Current.CancellationToken); // de novo recusado: não repete o aviso
            Assert.Equal(3, providerCalls);
            Assert.Equal(4, http.Requests.Count);
            Assert.Single(errors);
            w.Dispose();
        }

        [Fact]
        public async Task Provider_com_o_mesmo_hash_avisa_na_hora()
        {
            var (w, _, http, _) = Create((_, _) => (401, "{\"code\":\"ERROR\",\"data\":null,\"message\":\"WIDGET_USER_HASH_INVALID\"}"));
            var errors = new List<string>();
            w.Error += (_, e) => errors.Add(e.Code);
            await w.InitAsync(Config(c => c.UserHashProvider = _ => Task.FromResult<string?>("sempre-o-mesmo")), TestContext.Current.CancellationToken);
            Assert.Single(http.Requests); // hash igual: repetir não adianta
            Assert.Equal(new[] { Protocol.ErrorUserHashInvalid }, errors);
            w.Dispose();
        }

        [Fact]
        public async Task Outros_4xx_chegam_ao_onError_uma_vez_por_codigo_e_voltam_apos_sucesso()
        {
            var (w, _, _, _) = Create((_, i) => i switch
            {
                0 or 1 => (403, "{\"code\":\"ERROR\",\"data\":null,\"message\":\"WIDGET_ORIGIN_NOT_ALLOWED\"}"),
                2 => (404, ""),
                3 => (200, StubHandler.Envelope(Fixture("default"))),
                _ => (404, ""),
            });
            var errors = new List<string>();
            w.Error += (_, e) => errors.Add(e.Code);
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            await w.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "WIDGET_ORIGIN_NOT_ALLOWED" }, errors);
            await w.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "WIDGET_ORIGIN_NOT_ALLOWED", "HTTP_404" }, errors);
            await w.RefreshAsync(TestContext.Current.CancellationToken); // sucesso zera
            await w.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "WIDGET_ORIGIN_NOT_ALLOWED", "HTTP_404", "HTTP_404" }, errors);
            w.Dispose();
        }

        [Fact]
        public async Task Hash_renovado_aceito_nao_chama_onError()
        {
            var providerCalls = 0;
            var (w, _, http, _) = Create((r, _) =>
                r.Headers["X-bFocus-Widget-User"] == UserPayload.Base64(UserPayload.Json(
                    new BFocusUser { ExternalId = "USR-1" }, new BFocusCustomer { ExternalId = "ACME-1" }, "hash-2", "pt_BR"))
                    ? (200, StubHandler.Envelope(Fixture("default")))
                    : (401, "{\"code\":\"ERROR\",\"data\":null,\"message\":\"WIDGET_USER_HASH_INVALID\"}"));
            var errors = new List<string>();
            w.Error += (_, e) => errors.Add(e.Code);

            await w.InitAsync(Config(c => c.UserHashProvider = _ => Task.FromResult<string?>(++providerCalls == 1 ? "hash-1" : "hash-2")),
                TestContext.Current.CancellationToken);

            Assert.Equal(2, providerCalls); // init + uma renovação
            Assert.Equal(2, http.Requests.Count); // chamada + repetição
            Assert.Empty(errors); // a repetição passou: nada a avisar
            Assert.Equal("hash-2", w.Identity!.UserHash);
            Assert.Equal("v4.2.0", w.ReleaseNotes.Label);
            w.Dispose();
        }

        [Fact]
        public async Task Erro_de_identidade_sem_provider_nao_repete_o_aviso()
        {
            var (w, _, http, _) = Create((_, _) => (401, "{\"code\":\"ERROR\",\"data\":null,\"message\":\"WIDGET_VERIFIED_SESSION_REQUIRED\"}"));
            var errors = new List<string>();
            w.Error += (_, e) => errors.Add(e.Code);
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            await w.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, http.Requests.Count);
            Assert.Equal(new[] { Protocol.ErrorVerifiedSessionRequired }, errors);
            w.Dispose();
        }

        [Fact]
        public async Task Rede_e_5xx_sao_silenciosos()
        {
            var (w, _, _, _) = Create((_, i) => i == 0 ? throw new HttpRequestException("offline") : (503, "x"));
            var errors = new List<string>();
            w.Error += (_, e) => errors.Add(e.Code);
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            await w.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Empty(errors);
            Assert.Equal("", w.BadgeLabel);
            w.Dispose();
        }

        [Fact]
        public async Task Tempo_maximo_por_chamada()
        {
            var (w, _, http, _) = Create();
            http.Delay = TimeSpan.FromSeconds(5);
            w.Api.Timeout = TimeSpan.FromMilliseconds(200);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), sw.Elapsed.ToString());
            Assert.Null(w.LastLauncherState);
            w.Dispose();
        }

        [Fact]
        public async Task Open_espera_a_primeira_chamada()
        {
            var (w, host, http, _) = Create();
            http.Delay = TimeSpan.FromMilliseconds(400);
            var init = w.InitAsync(Config(), TestContext.Current.CancellationToken);
            await Wait.Until(() => w.IsInitialized);
            w.Open();
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.Empty(host.Shown); // a primeira chamada (que cria o usuário) ainda não voltou
            await init;
            await Wait.Until(() => host.Shown.Count == 1);
            w.Dispose();
        }

        [Fact]
        public async Task Consulta_periodica_com_o_widget_fechado_e_para_aberto()
        {
            var (w, _, http, _) = Create();
            w.PollIntervalOverride = TimeSpan.FromMilliseconds(80);
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            await Wait.Until(() => http.Requests.Count >= 3, what: "consultas periódicas");

            w.Open();
            await Wait.Until(() => w.IsOpen);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            var frozen = http.Requests.Count;
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.Equal(frozen, http.Requests.Count); // aberto: sem consulta

            w.SetAppForeground(false);
            w.Close();
            await Task.Delay(300, TestContext.Current.CancellationToken);
            var background = http.Requests.Count;
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.Equal(background, http.Requests.Count); // fundo: sem consulta

            w.SetAppForeground(true);
            await Wait.Until(() => http.Requests.Count >= background + 2, what: "volta ao primeiro plano");
            w.Dispose();
        }

        [Fact]
        public async Task Push_registra_depois_da_primeira_chamada_e_logout_cancela()
        {
            var (w, host, http, store) = Create((_, _) => (200, StubHandler.Envelope(Fixture("default"))));
            await w.RegisterPushTokenAsync("fcm-token-1", "android", TestContext.Current.CancellationToken); // antes do init: só guarda
            Assert.Empty(http.Requests);

            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            Assert.EndsWith("/api/v1/widget/launcher-state", http.Requests[0].Url);
            Assert.EndsWith("/api/v1/widget/push/devices", http.Requests[1].Url);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{\"token\":\"fcm-token-1\",\"platform\":\"android\",\"app_id\":\"com.empresa.erp\"}"), JsonNode.Parse(http.Requests[1].Body)));

            var key = w.Identity!.LastSeenStorageKey;
            await w.LogoutAsync(TestContext.Current.CancellationToken);
            Assert.EndsWith("/api/v1/widget/push/devices/unregister", http.Requests[^1].Url);
            Assert.Equal("{\"token\":\"fcm-token-1\"}", http.Requests[^1].Body);
            Assert.Null(store.Get(key));
            Assert.Contains("clear:https://widget.bfocus.com.br", host.Calls);
            Assert.False(w.IsInitialized);
            Assert.Throws<InvalidOperationException>(() => w.Open());
            w.Dispose();
        }

        [Fact]
        public async Task HandlePush_abre_no_chamado()
        {
            var (w, host, _, _) = Create();
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            Assert.False(w.HandlePush(new Dictionary<string, string> { ["foo"] = "bar" }));
            Assert.True(w.HandlePush(new Dictionary<string, string> { ["bfocus"] = "1", ["type"] = "ticket.reply", ["ticket_id"] = "t-1" }));
            await Wait.Until(() => host.Shown.Count == 1);
            Assert.EndsWith("&open=ticket%3At-1", host.Shown[0].Url);
            w.Dispose();
        }

        [Fact]
        public async Task Trocar_de_identidade_recria_as_WebViews_e_le_outra_base()
        {
            var store = new InMemoryStateStore();
            var (w, host, _, _) = Create(store: store);
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            w.Open();
            await Wait.Until(() => host.Shown.Count == 1);

            await w.InitAsync(Config(c => c.User.ExternalId = "USR-2"), TestContext.Current.CancellationToken);
            Assert.Contains("destroy:Tickets", host.Calls);
            Assert.False(w.IsOpen);
            Assert.Equal(2, store.Get("bf:lastSeen:bf_pk_test_123:USR-1:ACME-1") is null ? 0 : 2);
            Assert.NotNull(store.Get("bf:lastSeen:bf_pk_test_123:USR-2:ACME-1"));

            w.Open();
            await Wait.Until(() => host.Shown.Count == 2);
            Assert.True(host.Shown[1].ForceLoad);
            Assert.Contains("parentOrigin=app%3A%2F%2Fcom.empresa.erp", host.Shown[1].Url);
            w.Dispose();
        }

        [Fact]
        public async Task Erro_do_embed_com_hash_invalido_renova_e_recarrega_a_tela_visivel()
        {
            var n = 0;
            var (w, host, _, _) = Create();
            await w.InitAsync(Config(c => c.UserHashProvider = _ => Task.FromResult<string?>("h" + (++n))), TestContext.Current.CancellationToken);
            w.Open();
            await Wait.Until(() => host.Shown.Count == 1);
            host.Deliver(WidgetSurface.Tickets, "{\"type\":\"bfocus:error\",\"payload\":{\"code\":\"WIDGET_USER_HASH_INVALID\"}}");
            await Wait.Until(() => host.Shown.Count == 2, what: "recarga com hash novo");
            Assert.True(host.Shown[1].ForceLoad);
            Assert.Equal("h2", w.Identity!.UserHash);
            Assert.Equal(EmbedUrl.Build(w.Identity!), host.Shown[1].Url);
            w.Dispose();
        }

        [Fact]
        public async Task Eventos_vao_para_o_SynchronizationContext_capturado()
        {
            var ctx = new RecordingContext();
            var http = new StubHandler((_, _) => (200, StubHandler.Envelope(Fixture("default"))));
            var w = new BFocusWidget(new FakeHost(), new BFocusWidgetOptions
            {
                HttpClient = new HttpClient(http), StateStore = new InMemoryStateStore(), SynchronizationContext = ctx,
            });
            var onContext = false;
            w.PrimaryColorChanged += (_, _) => onContext = System.Threading.SynchronizationContext.Current == ctx;
            await w.InitAsync(Config(), TestContext.Current.CancellationToken);
            await Wait.Until(() => ctx.Posts > 0);
            ctx.Drain();
            Assert.True(onContext);
            w.Dispose();
        }

        private sealed class RecordingContext : System.Threading.SynchronizationContext
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<(System.Threading.SendOrPostCallback, object?)> _q = new();
            public int Posts => _q.Count;
            public override void Post(System.Threading.SendOrPostCallback d, object? state) => _q.Enqueue((d, state));
            public void Drain()
            {
                var prev = Current;
                SetSynchronizationContext(this);
                try { while (_q.TryDequeue(out var item)) item.Item1(item.Item2); }
                finally { SetSynchronizationContext(prev); }
            }
        }
    }
}
