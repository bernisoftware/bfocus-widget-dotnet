using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Bfocus.Widget
{
    /// <summary>
    /// Descrição de uma chamada ao bFocus (método, URL, headers na ordem, corpo). Separada do
    /// HttpClient para os testes compararem com <c>scenarios.launcherStateRequest</c>.
    /// </summary>
    public sealed class HttpRequestSpec
    {
        public HttpRequestSpec(string method, string url, IReadOnlyList<KeyValuePair<string, string>> headers, string body)
        {
            Method = method;
            Url = url;
            Headers = headers;
            Body = body;
        }

        public string Method { get; }
        public string Url { get; }
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }
        public string Body { get; }

        /// <summary>Headers comuns a toda chamada do pacote (§4 do protocolo).</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> IdentityHeaders(WidgetIdentity id) =>
            new[]
            {
                new KeyValuePair<string, string>("Content-Type", "application/json"),
                new KeyValuePair<string, string>(Protocol.HeaderKey, id.PublishableKey),
                new KeyValuePair<string, string>(Protocol.HeaderUser, id.UserBase64),
                new KeyValuePair<string, string>(Protocol.HeaderParentOrigin, id.ParentOrigin),
                new KeyValuePair<string, string>(Protocol.HeaderClient, id.Client),
            };

        /// <summary><c>POST {api}/api/v1/widget/launcher-state[?product=…&amp;audience=…]</c> com corpo <c>{}</c>.</summary>
        public static HttpRequestSpec LauncherState(WidgetIdentity id)
        {
            var q = new List<string>();
            if (id.Product != null) q.Add("product=" + Rfc3986.Encode(id.Product));
            if (id.Audience != null) q.Add("audience=" + Rfc3986.Encode(id.Audience));
            var url = id.ApiBaseUrl + "/api/v1/widget/launcher-state" + (q.Count > 0 ? "?" + string.Join("&", q) : string.Empty);
            return new HttpRequestSpec("POST", url, IdentityHeaders(id), "{}");
        }

        /// <summary><c>POST {api}/api/v1/widget/push/devices</c> com <c>{token, platform, app_id}</c>.</summary>
        public static HttpRequestSpec PushRegister(WidgetIdentity id, string token, string platform)
        {
            var body = new JsonObjectBuilder().Add("token", token).Add("platform", platform).Add("app_id", id.AppId.ToLowerInvariant()).ToString();
            return new HttpRequestSpec("POST", id.ApiBaseUrl + "/api/v1/widget/push/devices", IdentityHeaders(id), body);
        }

        /// <summary><c>POST {api}/api/v1/widget/push/devices/unregister</c> com <c>{token}</c>.</summary>
        public static HttpRequestSpec PushUnregister(WidgetIdentity id, string token)
        {
            var body = new JsonObjectBuilder().Add("token", token).ToString();
            return new HttpRequestSpec("POST", id.ApiBaseUrl + "/api/v1/widget/push/devices/unregister", IdentityHeaders(id), body);
        }

        /// <summary>
        /// Converte para <see cref="HttpRequestMessage"/>. O Content-Type sai exatamente
        /// <c>application/json</c>, sem <c>; charset=utf-8</c> (é o que o contrato fixa).
        /// </summary>
        public HttpRequestMessage ToHttpRequestMessage()
        {
            var msg = new HttpRequestMessage(new HttpMethod(Method), Url);
            var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(Body));
            foreach (var h in Headers)
            {
                if (h.Key == "Content-Type")
                    content.Headers.ContentType = new MediaTypeHeaderValue(h.Value);
                else
                    msg.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            msg.Content = content;
            return msg;
        }
    }
}
