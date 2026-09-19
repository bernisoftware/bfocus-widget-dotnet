using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Bfocus.Widget.Tests.Support;
using Xunit;

namespace Bfocus.Widget.Tests
{
    /// <summary>Casos de widgets-native/conformance/scenarios.json (§5.1 do BRIEF).</summary>
    public class ConformanceTests
    {
        public static TheoryData<string> UserPayloadCases() => Names("userPayload");
        public static TheoryData<string> EmbedUrlCases() => Names("embedUrl");
        public static TheoryData<string> LauncherStateCases() => Names("launcherStateRequest");
        public static TheoryData<string> BadgeCases() => Names("badge");
        public static TheoryData<string> PillCases() => Names("pill");

        public static TheoryData<string> PushHandleCases()
        {
            var d = new TheoryData<string>();
            foreach (var n in Scenarios.Doc["push"]!["handle"]!.AsArray()) d.Add(n!["name"]!.GetValue<string>());
            return d;
        }

        private static TheoryData<string> Names(string section)
        {
            var d = new TheoryData<string>();
            foreach (var n in Scenarios.Section(section)) d.Add(n["name"]!.GetValue<string>());
            return d;
        }

        private static JsonNode Case(string section, string name) =>
            Scenarios.Section(section).Single(n => n["name"]!.GetValue<string>() == name);

        [Theory]
        [MemberData(nameof(UserPayloadCases))]
        public void UserPayload_json_e_base64_identicos(string name)
        {
            var c = Case("userPayload", name);
            var id = Scenarios.Identity(c["config"]!.GetValue<string>());
            Assert.Equal(c["json"]!.GetValue<string>(), id.UserJson);
            Assert.Equal(c["base64"]!.GetValue<string>(), id.UserBase64);
        }

        [Theory]
        [MemberData(nameof(EmbedUrlCases))]
        public void EmbedUrl_identica(string name)
        {
            var c = Case("embedUrl", name);
            var id = Scenarios.Identity(c["config"]!.GetValue<string>());
            var page = c["page"]!.GetValue<string>() == "embed" ? EmbedPage.Tickets : EmbedPage.ReleaseNotes;
            var mode = c["mode"]!.GetValue<string>() == "browser" ? HostMode.Browser : HostMode.Native;
            var ids = c["ids"]?.AsArray().Select(x => x!.GetValue<string>()).ToList();
            var url = EmbedUrl.Build(id, page, mode, Scenarios.S(c, "open"), Scenarios.S(c, "view"), ids);
            Assert.Equal(c["expect"]!.GetValue<string>(), url);
        }

        [Fact]
        public void EmbedUrl_open_de_WidgetTarget_bate_com_o_cenario()
        {
            Assert.Equal("ticket:abc-123", WidgetTarget.Ticket("abc-123").ToOpenParam());
            Assert.Equal("chat", WidgetTarget.Chat("c-1").ToOpenParam());
            Assert.Equal("new", WidgetTarget.New.ToOpenParam());
            Assert.Null(WidgetTarget.List.ToOpenParam());
        }

        [Theory]
        [MemberData(nameof(LauncherStateCases))]
        public void LauncherStateRequest_identica(string name)
        {
            var c = Case("launcherStateRequest", name);
            var expect = c["expect"]!;
            var spec = HttpRequestSpec.LauncherState(Scenarios.Identity(c["config"]!.GetValue<string>()));
            Assert.Equal(expect["method"]!.GetValue<string>(), spec.Method);
            Assert.Equal(expect["url"]!.GetValue<string>(), spec.Url);
            Assert.Equal(expect["body"]!.GetValue<string>(), spec.Body);
            var expectedHeaders = expect["headers"]!.AsObject().Select(kv => (kv.Key, kv.Value!.GetValue<string>())).ToList();
            Assert.Equal(expectedHeaders, spec.Headers.Select(h => (h.Key, h.Value)).ToList()); // os cinco, na ordem
        }

        [Theory]
        [MemberData(nameof(LauncherStateCases))]
        public async Task LauncherStateRequest_no_fio_pelo_HttpClient(string name)
        {
            var c = Case("launcherStateRequest", name);
            var expect = c["expect"]!;
            var fixture = Scenarios.Doc["launcherStateFixtures"]!["default"]!.ToJsonString();
            var handler = new StubHandler((_, _) => (200, StubHandler.Envelope(fixture)));
            var api = new BFocusApiClient(new HttpClient(handler));

            var result = await api.FetchLauncherStateAsync(Scenarios.Identity(c["config"]!.GetValue<string>()), TestContext.Current.CancellationToken);

            Assert.Equal(ApiOutcome.Ok, result.Outcome);
            var r = Assert.Single(handler.Requests);
            Assert.Equal("POST", r.Method);
            Assert.Equal(expect["url"]!.GetValue<string>(), r.Url);
            Assert.Equal("{}", r.Body);
            foreach (var kv in expect["headers"]!.AsObject())
                Assert.Equal(kv.Value!.GetValue<string>(), r.Headers[kv.Key]); // Content-Type sem "; charset"
        }

        [Theory]
        [MemberData(nameof(BadgeCases))]
        public void Badge_passos(string name)
        {
            var c = Case("badge", name);
            var initial = c["initial"]!;
            var badge = new BadgeModel(Scenarios.S(initial, "last_seen"), initial["label"]!.GetValue<string>());
            var i = 0;
            foreach (var step in c["steps"]!.AsArray())
            {
                i++;
                var op = step!["op"]!.GetValue<string>();
                switch (op)
                {
                    case "state": badge.ApplyState(Scenarios.S(step, "latest_event_at")); break;
                    case "open": badge.Open(); break;
                    case "close": badge.Close(); break;
                    case "unread": badge.ApplyUnread(step["count"]!.GetValue<int>()); break;
                    case "seen": badge.ApplySeen(Scenarios.S(step, "latest_event_at")); break;
                    default: throw new InvalidOperationException("op desconhecida: " + op);
                }
                var expect = step["expect"]!.AsObject();
                if (expect.ContainsKey("label")) Assert.True(expect["label"]!.GetValue<string>() == badge.Label, $"passo {i} ({op}): label '{badge.Label}'");
                if (expect.ContainsKey("last_seen")) Assert.True(Scenarios.S(expect, "last_seen") == badge.LastSeen, $"passo {i} ({op}): last_seen '{badge.LastSeen}'");
            }
        }

        [Theory]
        [MemberData(nameof(PillCases))]
        public void Pilula(string name)
        {
            var c = Case("pill", name);
            var info = ReleaseNotesInfo.FromJson(Scenarios.ToElement(c["releaseNotes"]!));
            var state = ReleaseNotesState.From(info);
            var expect = c["expect"]!;
            Assert.Equal(expect["label"]!.GetValue<string>(), state.Label);
            Assert.Equal(expect["dot"]!.GetValue<bool>(), state.Dot);
            Assert.Equal(expect["bannerIds"]!.AsArray().Select(x => x!.GetValue<string>()), state.BannerIds);
        }

        [Fact]
        public void Push_register_e_unregister()
        {
            var reg = Scenarios.Doc["push"]!["register"]!;
            var id = Scenarios.Identity(reg["config"]!.GetValue<string>());
            var spec = HttpRequestSpec.PushRegister(id, reg["token"]!.GetValue<string>(), reg["platform"]!.GetValue<string>());
            Assert.Equal(reg["expect"]!["method"]!.GetValue<string>(), spec.Method);
            Assert.Equal(reg["expect"]!["url"]!.GetValue<string>(), spec.Url);
            Assert.True(JsonNode.DeepEquals(reg["expect"]!["json"], JsonNode.Parse(spec.Body)), spec.Body);
            // mesmos headers do launcher-state
            Assert.Equal(HttpRequestSpec.LauncherState(id).Headers, spec.Headers);

            var unreg = Scenarios.Doc["push"]!["unregister"]!;
            var id2 = Scenarios.Identity(unreg["config"]!.GetValue<string>());
            var spec2 = HttpRequestSpec.PushUnregister(id2, unreg["token"]!.GetValue<string>());
            Assert.Equal(unreg["expect"]!["method"]!.GetValue<string>(), spec2.Method);
            Assert.Equal(unreg["expect"]!["url"]!.GetValue<string>(), spec2.Url);
            Assert.True(JsonNode.DeepEquals(unreg["expect"]!["json"], JsonNode.Parse(spec2.Body)), spec2.Body);
        }

        [Theory]
        [MemberData(nameof(PushHandleCases))]
        public void Push_handle(string name)
        {
            var c = Scenarios.Doc["push"]!["handle"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == name)!;
            var data = c["data"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>());
            var result = PushMessage.Handle(data);
            var expect = c["expect"]!;
            Assert.Equal(expect["handled"]!.GetValue<bool>(), result.Handled);
            var nav = expect["navigate"];
            if (nav == null)
            {
                Assert.Null(result.Navigate);
            }
            else
            {
                Assert.NotNull(result.Navigate);
                Assert.True(JsonNode.DeepEquals(nav, JsonNode.Parse(result.Navigate!.ToNavigatePayloadJson())), result.Navigate.ToNavigatePayloadJson());
            }
        }

        [Fact]
        public void Fixtures_do_launcher_state_sao_lidas()
        {
            var def = LauncherState.FromJson(Scenarios.ToElement(Scenarios.Doc["launcherStateFixtures"]!["default"]!));
            Assert.Equal("#0EA5E9", def.PrimaryColor);
            Assert.Equal(2, def.Tickets.OpenCount);
            Assert.Equal("2026-09-01T12:00:00+00:00", def.Tickets.LatestEventAt);
            Assert.True(def.Chat.Enabled);
            Assert.Equal("4.2.0", def.ReleaseNotes.BadgeVersion);
            Assert.Equal("#22C55E", def.ReleaseNotes.Product!.Color);

            var empty = LauncherState.FromJson(Scenarios.ToElement(Scenarios.Doc["launcherStateFixtures"]!["empty"]!));
            Assert.Null(empty.Tickets.LatestEventAt);
            Assert.Null(empty.ReleaseNotes.Product);
            Assert.Empty(empty.ReleaseNotes.BannerIds);
        }

        [Fact]
        public void Protocolo_e_versao_do_arquivo()
        {
            Assert.Equal(Protocol.HostProtocol, Scenarios.Doc["hostProtocol"]!.GetValue<int>());
            Assert.Equal(Protocol.DefaultApiBaseUrl, Scenarios.Defaults("apiBaseUrl"));
            Assert.Equal(Protocol.DefaultEmbedBaseUrl, Scenarios.Defaults("embedBaseUrl"));
            Assert.Equal(Protocol.DefaultLocale, Scenarios.Defaults("locale"));
            Assert.Equal(Protocol.DefaultPollIntervalSeconds, Scenarios.Doc["defaults"]!["pollIntervalSeconds"]!.GetValue<int>());
        }
    }
}
