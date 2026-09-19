using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Bfocus.Widget.Tests.Support;
using Xunit;

namespace Bfocus.Widget.Tests
{
    public class EncodingTests
    {
        [Theory]
        [InlineData("abcXYZ019-._~", "abcXYZ019-._~")]
        [InlineData("a b", "a%20b")]
        [InlineData("!'()*", "%21%27%28%29%2A")]
        [InlineData("app://com.empresa.erp", "app%3A%2F%2Fcom.empresa.erp")]
        [InlineData("Açaí", "A%C3%A7a%C3%AD")]
        [InlineData("a,b", "a%2Cb")]
        [InlineData("x=1&y=+", "x%3D1%26y%3D%2B")]
        public void Rfc3986_codifica_so_o_reservado(string input, string expected) =>
            Assert.Equal(expected, Rfc3986.Encode(input));

        [Theory]
        [InlineData("Açaí & Cia", "\"Açaí & Cia\"")]
        [InlineData("a\"b\\c", "\"a\\\"b\\\\c\"")]
        [InlineData("l1\nl2\t\r\b\f", "\"l1\\nl2\\t\\r\\b\\f\"")]
        [InlineData("\u0001\u001f", "\"\\u0001\\u001f\"")]
        [InlineData("<script>/</script>", "\"<script>/</script>\"")]
        [InlineData("😀", "\"😀\"")]
        public void JsonText_igual_ao_JSON_stringify(string input, string expected) =>
            Assert.Equal(expected, JsonText.Quote(input));

        [Fact]
        public void JsonText_surrogate_solto_vira_escape()
        {
            // Fora do InlineData: a serialização dos dados de teste troca surrogate solto por U+FFFD.
            Assert.Equal("\"\\ud800x\"", JsonText.Quote("\ud800x"));
            Assert.Equal("\"x\\udc00\"", JsonText.Quote("x\udc00"));
        }

        [Fact]
        public void Payload_omite_vazios_e_mantem_locale()
        {
            var json = UserPayload.Json(
                new BFocusUser { ExternalId = "U", Name = "", Email = null },
                new BFocusCustomer { ExternalId = "C", Website = "" },
                userHash: "", locale: "es");
            Assert.Equal("{\"externalId\":\"U\",\"locale\":\"es\",\"customer\":{\"externalId\":\"C\"}}", json);
        }

        [Fact]
        public void Recarga_quando_so_o_fragmento_muda()
        {
            const string a = "https://widget.bfocus.com.br/v1/release-notes.html#host=native&ids=1";
            const string b = "https://widget.bfocus.com.br/v1/release-notes.html#host=native&ids=2";
            Assert.True(EmbedUrl.IsSameDocument(a, b));
            Assert.False(EmbedUrl.IsSameDocument("about:blank", b));
            Assert.False(EmbedUrl.IsSameDocument(null, b));
            Assert.False(EmbedUrl.IsSameDocument("https://widget.bfocus.com.br/v1/embed.html#x", b));
            var fresh = EmbedUrl.WithReloadToken(b, 42);
            Assert.Equal("https://widget.bfocus.com.br/v1/release-notes.html?bfr=42#host=native&ids=2", fresh);
            Assert.False(EmbedUrl.IsSameDocument(b, fresh));
            Assert.Equal("https://x/p.html?bfr=7#f", EmbedUrl.WithReloadToken("https://x/p.html?bfr=1#f", 7));
            Assert.True(new EmbedOrigin("https://widget.bfocus.com.br/v1").IsSameOrigin(fresh));
        }

        [Fact]
        public void HostScript_monta_o_receive_com_guarda()
        {
            Assert.Equal("window.bFocusEmbed && window.bFocusEmbed.receive({\"type\":\"bfocus:open\"})", HostScript.Receive(MessageTypes.Open));
            Assert.Equal(
                "window.bFocusEmbed && window.bFocusEmbed.receive({\"type\":\"bfocus:navigate\",\"payload\":{\"view\":\"ticket\",\"ticketId\":\"t-1\"}})",
                HostScript.Receive(MessageTypes.Navigate, WidgetTarget.Ticket("t-1").ToNavigatePayloadJson()));
        }
    }

    public class ConfigTests
    {
        private static BFocusConfig Valid() => Scenarios.ToConfig(Scenarios.Config("min"));

        [Fact]
        public void Config_minima_valida() => Valid().Validate();

        [Theory]
        [InlineData("bf_whs_abc")]
        [InlineData("bf_live_abc")]
        [InlineData("bf_sk_abc")]
        [InlineData("xyz")]
        [InlineData("")]
        public void Recusa_chave_que_nao_e_publica(string key)
        {
            var c = Valid();
            c.PublishableKey = key;
            Assert.Throws<ArgumentException>(c.Validate);
        }

        [Theory]
        [InlineData("http://api.bfocus.com.br")]
        [InlineData("ftp://127.0.0.1")]
        [InlineData("api.bfocus.com.br")]
        public void Recusa_url_insegura(string url)
        {
            var c = Valid();
            c.ApiBaseUrl = url;
            Assert.Throws<ArgumentException>(c.Validate);
        }

        [Theory]
        [InlineData("http://127.0.0.1:8787")]
        [InlineData("http://localhost:1234/v1")]
        [InlineData("https://widget.homolog.bfocus.com.br/v1")]
        public void Aceita_https_ou_http_local(string url)
        {
            var c = Valid();
            c.EmbedBaseUrl = url;
            c.Validate();
        }

        [Fact]
        public void Exige_appId_user_customer_e_audience_valida()
        {
            var c = Valid(); c.AppId = " "; Assert.Throws<ArgumentException>(c.Validate);
            c = Valid(); c.User.ExternalId = ""; Assert.Throws<ArgumentException>(c.Validate);
            c = Valid(); c.Customer.ExternalId = ""; Assert.Throws<ArgumentException>(c.Validate);
            c = Valid(); c.Audience = "todos"; Assert.Throws<ArgumentException>(c.Validate);
            c = Valid(); c.UserHash = "bf_whs_x"; Assert.Throws<ArgumentException>(c.Validate);
        }

        [Theory]
        [InlineData("pt_BR", "pt_BR")]
        [InlineData("pt-PT", "pt_BR")]
        [InlineData("es-AR", "es")]
        [InlineData("en", "en")]
        [InlineData("fr-FR", "en")]
        public void Locale_mapeado(string input, string expected) => Assert.Equal(expected, LocaleResolver.Normalize(input));

        [Fact]
        public void Locale_padrao_vem_da_cultura()
        {
            Assert.Equal("pt_BR", LocaleResolver.FromCulture(new CultureInfo("pt-BR")));
            Assert.Equal("es", LocaleResolver.FromCulture(new CultureInfo("es-MX")));
            Assert.Equal("en", LocaleResolver.FromCulture(new CultureInfo("de-DE")));
            Assert.Equal("pt_BR", LocaleResolver.FromCulture(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Versao_do_pacote_bate_com_o_assembly()
        {
            var v = typeof(BFocusWidget).Assembly.GetName().Version!;
            Assert.Equal(Protocol.PackageVersion, $"{v.Major}.{v.Minor}.{v.Build}");
            Assert.Equal("dotnet/" + Protocol.PackageVersion, Protocol.Client);
            var info = typeof(BFocusWidget).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            Assert.StartsWith(Protocol.PackageVersion, info);
        }
    }

    public class OriginAndFilesTests
    {
        [Fact]
        public void Origem_do_embed()
        {
            var o = new EmbedOrigin("https://widget.bfocus.com.br/v1");
            Assert.Equal("https://widget.bfocus.com.br", o.Value);
            Assert.True(o.IsSameOrigin("https://widget.bfocus.com.br/v1/embed.html#host=native"));
            Assert.True(o.IsSameOrigin("https://WIDGET.bfocus.com.br:443/x"));
            Assert.False(o.IsSameOrigin("https://widget.bfocus.com.br.evil.com/v1/embed.html"));
            Assert.False(o.IsSameOrigin("http://widget.bfocus.com.br/v1/embed.html"));
            Assert.False(o.IsSameOrigin("https://widget.bfocus.com.br:8443/"));
            Assert.False(o.IsSameOrigin("about:blank"));
            Assert.False(o.IsSameOrigin(null));

            var local = new EmbedOrigin("http://127.0.0.1:8787/v1");
            Assert.Equal("http://127.0.0.1:8787", local.Value);
            Assert.True(local.IsSameOrigin("http://127.0.0.1:8787/v1/release-notes.html"));
            Assert.False(local.IsSameOrigin("http://127.0.0.1:8788/v1/release-notes.html"));
        }

        [Theory]
        [InlineData("https://example.com", true)]
        [InlineData("mailto:a@b.com", true)]
        [InlineData("tel:+5511", true)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("file:///etc/passwd", false)]
        [InlineData("data:text/html,x", false)]
        [InlineData("", false)]
        public void Links_externos_seguros(string url, bool ok) => Assert.Equal(ok, EmbedOrigin.IsExternalSafe(url));

        [Theory]
        [InlineData("manual.pdf", "manual.pdf")]
        [InlineData("../../evil.exe", "evil.exe")]
        [InlineData("C:\\x\\y.txt", "y.txt")]
        [InlineData("a:b*c?.pdf", "a_b_c_.pdf")]
        [InlineData("   ", "download")]
        [InlineData(null, "download")]
        [InlineData("..", "download")]
        public void Nome_de_arquivo_sanitizado(string? input, string expected) => Assert.Equal(expected, FileNames.Sanitize(input));

        [Theory]
        [InlineData("#6366F1", true, 0x63, 0x66, 0xF1)]
        [InlineData("0ea5e9", true, 0x0E, 0xA5, 0xE9)]
        [InlineData("#abc", true, 0xAA, 0xBB, 0xCC)]
        [InlineData("", false, 0, 0, 0)]
        [InlineData("#12345", false, 0, 0, 0)]
        public void Cor_hex(string input, bool ok, int r, int g, int b)
        {
            Assert.Equal(ok, ColorHex.TryParse(input, out var rr, out var gg, out var bb));
            if (ok) Assert.Equal((r, g, b), (rr, gg, bb));
        }

        [Fact]
        public void Mensagem_do_embed_so_com_tipo_bfocus()
        {
            Assert.True(EmbedMessage.TryParse("{\"type\":\"bfocus:unread\",\"payload\":{\"count\":3}}", out var m));
            Assert.Equal(3, m!.GetInt("count"));
            Assert.True(EmbedMessage.TryParse("{\"type\":\"bfocus:unread\",\"payload\":{\"count\":\"7\"}}", out m));
            Assert.Equal(7, m!.GetInt("count"));
            Assert.False(EmbedMessage.TryParse("{\"type\":\"outro:x\"}", out _));
            Assert.False(EmbedMessage.TryParse("[1,2]", out _));
            Assert.False(EmbedMessage.TryParse("nao json", out _));
            Assert.False(EmbedMessage.TryParse(null, out _));
        }

        [Fact]
        public void Envelope_de_erro()
        {
            Assert.Equal(ApiOutcome.IdentityError, BFocusApiClient.Interpret(401, "{\"code\":\"ERROR\",\"data\":null,\"message\":\"WIDGET_USER_HASH_INVALID\"}", true).Outcome);
            Assert.Equal(ApiOutcome.IdentityError, BFocusApiClient.Interpret(401, "{\"detail\":\"WIDGET_VERIFIED_SESSION_REQUIRED\"}", true).Outcome);
            Assert.Equal(ApiOutcome.HttpError, BFocusApiClient.Interpret(401, "{\"message\":\"WIDGET_KEY_INVALID\"}", true).Outcome);
            Assert.Equal(ApiOutcome.Transient, BFocusApiClient.Interpret(503, "<html>", true).Outcome);
            Assert.Equal(ApiOutcome.Transient, BFocusApiClient.Interpret(429, "", true).Outcome);
            Assert.Equal(ApiOutcome.Transient, BFocusApiClient.Interpret(200, "{\"code\":\"OK\"}", true).Outcome);
            Assert.Equal("X", BFocusApiClient.ExtractCode("{\"detail\":{\"code\":\"X\"}}"));
        }
    }

    public class StateStoreTests
    {
        [Fact]
        public void Arquivo_json_ida_e_volta()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bfocus-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var path = Path.Combine(dir, "sub", "state.json");
                var store = new FileStateStore(path);
                Assert.Null(store.Get("a"));
                store.Set("a", "1");
                store.Set("bf:lastSeen:k:u:c", "2026-09-01T10:00:00+00:00");
                store.Set("a", "2");
                Assert.True(File.Exists(path));

                var again = new FileStateStore(path);
                Assert.Equal("2", again.Get("a"));
                Assert.Equal("2026-09-01T10:00:00+00:00", again.Get("bf:lastSeen:k:u:c"));
                again.Set("a", null);
                Assert.Null(new FileStateStore(path).Get("a"));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Arquivo_corrompido_recomeca()
        {
            var path = Path.Combine(Path.GetTempPath(), "bfocus-bad-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{isto nao e json");
            try
            {
                var store = new FileStateStore(path);
                Assert.Null(store.Get("x"));
                store.Set("x", "y");
                Assert.Equal("y", new FileStateStore(path).Get("x"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Caminho_padrao_em_bfocus()
        {
            Assert.Equal("bfocus", new DirectoryInfo(FileStateStore.DefaultDirectory()).Name);
            Assert.EndsWith("widget-state.json", FileStateStore.DefaultFilePath());
        }
    }

    public class StringsTests
    {
        [Fact]
        public void Notificacao_nos_tres_idiomas()
        {
            Assert.Equal(("Suporte", "Há novidades nos seus chamados."), (WidgetStrings.For("pt_BR").NotificationTitle, WidgetStrings.For("pt_BR").NotificationBody));
            Assert.Equal(("Support", "There are updates on your tickets."), (WidgetStrings.For("en").NotificationTitle, WidgetStrings.For("en").NotificationBody));
            Assert.Equal(("Soporte", "Hay novedades en tus tickets."), (WidgetStrings.For("es").NotificationTitle, WidgetStrings.For("es").NotificationBody));
        }
    }
}
