using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Bfocus.Widget
{
    /// <summary>Resposta (<c>data</c>) do <c>POST /api/v1/widget/launcher-state</c>.</summary>
    public sealed class LauncherState
    {
        public string? PrimaryColor { get; set; }
        public TicketsSummary Tickets { get; set; } = new TicketsSummary();
        public ChatSummary Chat { get; set; } = new ChatSummary();
        public ReleaseNotesInfo ReleaseNotes { get; set; } = new ReleaseNotesInfo();

        /// <summary>Lê o <c>data</c> do envelope. Campos ausentes ficam no padrão (o contrato só cresce).</summary>
        public static LauncherState FromJson(JsonElement data)
        {
            var s = new LauncherState();
            if (data.ValueKind != JsonValueKind.Object) return s;
            s.PrimaryColor = Json.GetString(data, "primary_color");
            if (data.TryGetProperty("tickets", out var t) && t.ValueKind == JsonValueKind.Object)
            {
                s.Tickets.OpenCount = Json.GetInt(t, "open_count");
                s.Tickets.TotalCount = Json.GetInt(t, "total_count");
                s.Tickets.LatestEventAt = Json.GetString(t, "latest_event_at");
            }
            if (data.TryGetProperty("chat", out var c) && c.ValueKind == JsonValueKind.Object)
            {
                s.Chat.Enabled = Json.GetBool(c, "enabled");
                s.Chat.ActiveConversationId = Json.GetString(c, "active_conversation_id");
            }
            if (data.TryGetProperty("release_notes", out var rn))
                s.ReleaseNotes = ReleaseNotesInfo.FromJson(rn);
            return s;
        }

        public static LauncherState FromJson(string dataJson)
        {
            using (var doc = JsonDocument.Parse(dataJson))
                return FromJson(doc.RootElement);
        }
    }

    public sealed class TicketsSummary
    {
        public int OpenCount { get; set; }
        public int TotalCount { get; set; }
        /// <summary>ISO 8601 com fuso; <c>null</c> quando o usuário não tem eventos.</summary>
        public string? LatestEventAt { get; set; }
    }

    public sealed class ChatSummary
    {
        public bool Enabled { get; set; }
        public string? ActiveConversationId { get; set; }
    }

    public sealed class ReleaseProduct
    {
        public string? Slug { get; set; }
        public string? Name { get; set; }
        public string? Color { get; set; }
    }

    /// <summary>Bloco <c>release_notes</c> do launcher-state.</summary>
    public sealed class ReleaseNotesInfo
    {
        public string? BadgeVersion { get; set; }
        public ReleaseProduct? Product { get; set; }
        public IReadOnlyList<string> BannerIds { get; set; } = Array.Empty<string>();
        public bool HasUnseen { get; set; }

        public static ReleaseNotesInfo FromJson(JsonElement rn)
        {
            var r = new ReleaseNotesInfo();
            if (rn.ValueKind != JsonValueKind.Object) return r;
            r.BadgeVersion = Json.GetString(rn, "badge_version");
            r.HasUnseen = Json.GetBool(rn, "has_unseen");
            if (rn.TryGetProperty("product", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                r.Product = new ReleaseProduct
                {
                    Slug = Json.GetString(p, "slug"),
                    Name = Json.GetString(p, "name"),
                    Color = Json.GetString(p, "color"),
                };
            }
            if (rn.TryGetProperty("banner_ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var id in ids.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(id.GetString())) list.Add(id.GetString()!);
                }
                r.BannerIds = list;
            }
            return r;
        }
    }

    /// <summary>Leitura tolerante de JSON (tipos errados viram padrão em vez de exceção).</summary>
    internal static class Json
    {
        public static string? GetString(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)) return null;
            return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        public static int GetInt(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number)
            {
                if (v.TryGetInt32(out var i)) return i;
                if (v.TryGetDouble(out var d)) return d > int.MaxValue ? int.MaxValue : d < int.MinValue ? int.MinValue : (int)d;
            }
            if (v.ValueKind == JsonValueKind.String &&
                int.TryParse(v.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var s))
                return s;
            return 0;
        }

        public static bool GetBool(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)) return false;
            return v.ValueKind == JsonValueKind.True;
        }
    }
}
