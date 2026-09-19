using System;
using System.Globalization;

namespace Bfocus.Widget
{
    /// <summary>
    /// Modelo do badge do botão, idêntico ao widget.js (<c>scenarios.badge</c>):
    /// <list type="bullet">
    /// <item><c>state(latest)</c>: só com o widget FECHADO. latest nulo não faz nada; sem last_seen,
    /// grava e não acende; <c>latest &gt; last_seen</c> (como instante) acende "•".</item>
    /// <item><c>unread(count)</c>: label = count ("99+" acima de 99, "" em 0).</item>
    /// <item><c>seen(ts)</c>: last_seen = ts.</item>
    /// <item><c>open</c>/<c>close</c>: aberto, o state é ignorado; nenhum dos dois muda o label.</item>
    /// </list>
    /// </summary>
    public sealed class BadgeModel
    {
        public const string Dot = "•";

        public BadgeModel(string? lastSeen = null, string label = "")
        {
            LastSeen = string.IsNullOrEmpty(lastSeen) ? null : lastSeen;
            Label = label ?? string.Empty;
        }

        public string Label { get; private set; }
        public string? LastSeen { get; private set; }
        public bool IsOpen { get; private set; }

        public void Open() => IsOpen = true;

        public void Close() => IsOpen = false;

        /// <summary>Resposta do launcher-state.</summary>
        public void ApplyState(string? latestEventAt)
        {
            if (IsOpen) return; // aberto, quem manda no badge é o bfocus:unread
            if (string.IsNullOrEmpty(latestEventAt)) return;
            if (LastSeen == null)
            {
                LastSeen = latestEventAt; // primeira visita: só a base, sem acender
                return;
            }
            if (CompareInstants(latestEventAt!, LastSeen) > 0) Label = Dot;
        }

        /// <summary><c>bfocus:unread</c>.</summary>
        public void ApplyUnread(int count) => Label = FormatCount(count);

        /// <summary><c>bfocus:seen</c>.</summary>
        public void ApplySeen(string? latestEventAt)
        {
            if (!string.IsNullOrEmpty(latestEventAt)) LastSeen = latestEventAt;
        }

        public static string FormatCount(int count) =>
            count <= 0 ? string.Empty : count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);

        /// <summary>Compara datas ISO 8601 como instante (fusos diferentes); texto só se não der para ler.</summary>
        public static int CompareInstants(string a, string b)
        {
            if (TryParse(a, out var da) && TryParse(b, out var db)) return da.UtcTicks.CompareTo(db.UtcTicks);
            return string.CompareOrdinal(a, b);
        }

        private static bool TryParse(string s, out DateTimeOffset value) =>
            DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }
}
