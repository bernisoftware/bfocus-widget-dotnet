using System;

namespace Bfocus.Widget
{
    /// <summary>Tela inicial / destino do <c>bfocus:navigate</c>.</summary>
    public sealed class WidgetTarget : IEquatable<WidgetTarget>
    {
        private WidgetTarget(string view, string? ticketId, string? conversationId)
        {
            View = view;
            TicketId = ticketId;
            ConversationId = conversationId;
        }

        /// <summary><c>list</c> | <c>new</c> | <c>chat</c> | <c>ticket</c>.</summary>
        public string View { get; }
        public string? TicketId { get; }
        public string? ConversationId { get; }

        public static WidgetTarget List { get; } = new WidgetTarget("list", null, null);
        public static WidgetTarget New { get; } = new WidgetTarget("new", null, null);

        public static WidgetTarget Chat(string? conversationId = null) =>
            new WidgetTarget("chat", null, string.IsNullOrEmpty(conversationId) ? null : conversationId);

        public static WidgetTarget Ticket(string ticketId)
        {
            if (string.IsNullOrEmpty(ticketId)) throw new ArgumentException("ticketId vazio.", nameof(ticketId));
            return new WidgetTarget("ticket", ticketId, null);
        }

        /// <summary>Valor do parâmetro <c>open=</c> no primeiro carregamento (<c>null</c> = lista, o padrão).</summary>
        public string? ToOpenParam()
        {
            switch (View)
            {
                case "ticket": return "ticket:" + TicketId;
                case "chat": return "chat";
                case "new": return "new";
                default: return null;
            }
        }

        /// <summary>Payload do <c>bfocus:navigate</c>: <c>{view, ticketId?, conversationId?}</c>.</summary>
        public string ToNavigatePayloadJson() =>
            new JsonObjectBuilder()
                .Add("view", View)
                .AddIfNotEmpty("ticketId", TicketId)
                .AddIfNotEmpty("conversationId", ConversationId)
                .ToString();

        public bool Equals(WidgetTarget? other) =>
            other != null && other.View == View && other.TicketId == TicketId && other.ConversationId == ConversationId;

        public override bool Equals(object? obj) => Equals(obj as WidgetTarget);

        public override int GetHashCode() =>
            (View.GetHashCode() * 397) ^ (TicketId?.GetHashCode() ?? 0) ^ ((ConversationId?.GetHashCode() ?? 0) * 17);

        public override string ToString() => ToNavigatePayloadJson();
    }
}
