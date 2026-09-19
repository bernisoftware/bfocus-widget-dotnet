using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bfocus.Widget
{
    /// <summary>Resultado do <see cref="PushMessage.Handle(IEnumerable{KeyValuePair{string,string}})"/>.</summary>
    public sealed class PushHandleResult
    {
        internal PushHandleResult(bool handled, WidgetTarget? navigate)
        {
            Handled = handled;
            Navigate = navigate;
        }

        /// <summary><c>true</c> quando a notificação é do bFocus (<c>data.bfocus == "1"</c>).</summary>
        public bool Handled { get; }
        /// <summary>Para onde o widget navega (payload do <c>bfocus:navigate</c>); <c>null</c> se não for do bFocus.</summary>
        public WidgetTarget? Navigate { get; }
    }

    /// <summary>
    /// Leitura do <c>data</c> do push FCM que o bFocus envia (§4.4 do BRIEF). O pacote não depende do
    /// Firebase: o app entrega o dicionário que recebeu.
    /// </summary>
    public static class PushMessage
    {
        public static PushHandleResult Handle(IEnumerable<KeyValuePair<string, string>> data)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (data != null)
                foreach (var kv in data) if (kv.Key != null) d[kv.Key] = kv.Value;
            return HandleCore(d);
        }

        /// <summary>Variante para dicionários com valores <c>object</c> (userInfo do iOS, Bundle convertido).</summary>
        public static PushHandleResult Handle(IEnumerable<KeyValuePair<string, object>> data)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (data != null)
                foreach (var kv in data)
                    if (kv.Key != null && kv.Value != null) d[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            return HandleCore(d);
        }

        private static PushHandleResult HandleCore(Dictionary<string, string> d)
        {
            if (!d.TryGetValue("bfocus", out var flag) || flag != "1") return new PushHandleResult(false, null);
            d.TryGetValue("type", out var type);
            d.TryGetValue("ticket_id", out var ticketId);
            d.TryGetValue("conversation_id", out var conversationId);
            switch (type)
            {
                case "ticket.reply":
                case "ticket.status":
                    return new PushHandleResult(true, string.IsNullOrEmpty(ticketId) ? WidgetTarget.List : WidgetTarget.Ticket(ticketId!));
                case "chat.message":
                    return new PushHandleResult(true, WidgetTarget.Chat(conversationId));
                default:
                    // Tipo novo (contrato aditivo): abre a lista em vez de ignorar.
                    return new PushHandleResult(true, WidgetTarget.List);
            }
        }
    }
}
