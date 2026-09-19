namespace Bfocus.Widget
{
    /// <summary>
    /// JavaScript que o pacote executa na página (canal pacote → embed). O guarda
    /// <c>window.bFocusEmbed &amp;&amp;</c> evita erro enquanto a página ainda não carregou.
    /// </summary>
    public static class HostScript
    {
        /// <summary>Mensagem como objeto JSON: <c>{"type":"…","payload":{…}}</c>.</summary>
        public static string MessageJson(string type, string? payloadJson = null)
        {
            var b = new JsonObjectBuilder().Add("type", type);
            if (payloadJson != null) b.AddRaw("payload", payloadJson);
            return b.ToString();
        }

        /// <summary><c>window.bFocusEmbed &amp;&amp; window.bFocusEmbed.receive({...})</c>.</summary>
        public static string Receive(string type, string? payloadJson = null) =>
            "window.bFocusEmbed && window.bFocusEmbed.receive(" + MessageJson(type, payloadJson) + ")";

        /// <summary>Força a entrega das mensagens que o embed guardou antes do canal existir.</summary>
        public const string Flush = "window.bFocusEmbed && window.bFocusEmbed.flush()";

        /// <summary>
        /// Canal genérico (<c>window.bFocusHost</c>) para hosts sem canal próprio. WebView2 não precisa
        /// (tem <c>chrome.webview</c>); fica aqui para quem implementar outro host.
        /// </summary>
        public static string InjectGenericChannel(string postFunctionExpression) =>
            "window.bFocusHost = { postMessage: function (s) { " + postFunctionExpression + "(s) } }; " + Flush;
    }
}
