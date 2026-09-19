using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bfocus.Widget.Tests.Support
{
    /// <summary>Acesso ao scenarios.json (cópia do pacote, gerada pelo conformance/generate.mjs).</summary>
    internal static class Scenarios
    {
        private static readonly Lazy<JsonNode> Root = new Lazy<JsonNode>(() =>
            JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenarios.json")))!);

        public static JsonNode Doc => Root.Value;

        public static IEnumerable<JsonNode> Section(string name) => Doc[name]!.AsArray().Select(n => n!);

        public static JsonNode Config(string name) => Doc["configs"]![name]!;

        public static string Defaults(string key) => Doc["defaults"]![key]!.GetValue<string>();

        /// <summary>Converte uma config do cenário em <see cref="BFocusConfig"/> (padrões do <c>defaults</c>).</summary>
        public static BFocusConfig ToConfig(JsonNode c, string? apiBaseUrl = null, string? embedBaseUrl = null)
        {
            var u = c["user"]!;
            var cu = c["customer"]!;
            return new BFocusConfig
            {
                PublishableKey = S(c, "publishableKey")!,
                AppId = S(c, "appId")!,
                User = new BFocusUser { ExternalId = S(u, "externalId")!, Name = S(u, "name"), Email = S(u, "email"), Phone = S(u, "phone") },
                Customer = new BFocusCustomer
                {
                    ExternalId = S(cu, "externalId")!, Name = S(cu, "name"), Document = S(cu, "document"),
                    Email = S(cu, "email"), Phone = S(cu, "phone"), Website = S(cu, "website"),
                },
                UserHash = S(c, "userHash"),
                Locale = S(c, "locale") ?? Defaults("locale"),
                Product = S(c, "product"),
                Audience = S(c, "audience"),
                ApiBaseUrl = apiBaseUrl ?? S(c, "apiBaseUrl") ?? Defaults("apiBaseUrl"),
                EmbedBaseUrl = embedBaseUrl ?? S(c, "embedBaseUrl") ?? Defaults("embedBaseUrl"),
                ShowReleaseNotes = c["showReleaseNotes"]?.GetValue<bool>() ?? true,
            };
        }

        public static WidgetIdentity Identity(string configName)
        {
            var c = Config(configName);
            return WidgetIdentity.Create(ToConfig(c), S(c, "client")!);
        }

        public static string? S(JsonNode? n, string key)
        {
            var v = n?[key];
            return v == null || v.GetValueKind() != JsonValueKind.String ? null : v.GetValue<string>();
        }

        public static JsonElement ToElement(JsonNode node)
        {
            using var doc = JsonDocument.Parse(node.ToJsonString());
            return doc.RootElement.Clone();
        }
    }
}
