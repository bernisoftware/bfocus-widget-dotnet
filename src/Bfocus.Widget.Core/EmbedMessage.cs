using System;
using System.Globalization;
using System.Text.Json;

namespace Bfocus.Widget
{
    /// <summary>Mensagem embed → pacote: string JSON <c>{"type":"bfocus:…","payload":{…}}</c>.</summary>
    public sealed class EmbedMessage
    {
        private readonly JsonElement _payload;

        private EmbedMessage(string type, JsonElement payload)
        {
            Type = type;
            _payload = payload;
        }

        public string Type { get; }

        public bool HasPayload => _payload.ValueKind == JsonValueKind.Object;

        /// <summary>
        /// Lê a mensagem. Recusa o que não for objeto com <c>type</c> começando por <c>bfocus:</c>
        /// (mesma regra do <c>hostBridge.ts</c>).
        /// </summary>
        public static bool TryParse(string? json, out EmbedMessage? message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(json) || json!.Length > 1_000_000) return false;
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return false;
                    var type = Json.GetString(root, "type");
                    if (type == null || !type.StartsWith("bfocus:", StringComparison.Ordinal)) return false;
                    var payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;
                    message = new EmbedMessage(type, payload);
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public string? GetString(string name) => HasPayload ? Json.GetString(_payload, name) : null;

        /// <summary>Número do payload (aceita número ou texto numérico).</summary>
        public int? GetInt(string name)
        {
            if (!HasPayload || !_payload.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number)
            {
                if (v.TryGetInt32(out var i)) return i;
                if (v.TryGetDouble(out var d)) return d > int.MaxValue ? int.MaxValue : (int)Math.Max(d, int.MinValue);
            }
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                return s;
            return null;
        }

        public override string ToString() => Type;
    }
}
