using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bfocus.Widget.Tests.Support
{
    /// <summary>Requisição capturada (headers achatados, corpo lido).</summary>
    internal sealed record CapturedRequest(string Method, string Url, Dictionary<string, string> Headers, string Body);

    /// <summary>HttpMessageHandler que responde por função e guarda as requisições.</summary>
    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<CapturedRequest, int, (int Status, string Body)> _respond;
        private readonly object _lock = new object();

        public StubHandler(Func<CapturedRequest, int, (int Status, string Body)> respond) { _respond = respond; }

        public List<CapturedRequest> Requests { get; } = new();

        /// <summary>Atraso artificial (testa a serialização da primeira chamada).</summary>
        public TimeSpan Delay { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in request.Headers) headers[h.Key] = string.Join(",", h.Value);
            var body = "";
            if (request.Content != null)
            {
                foreach (var h in request.Content.Headers) headers[h.Key] = string.Join(",", h.Value);
                body = await request.Content.ReadAsStringAsync(ct);
            }
            var captured = new CapturedRequest(request.Method.Method, request.RequestUri!.ToString(), headers, body);
            int index;
            lock (_lock)
            {
                Requests.Add(captured);
                index = Requests.Count - 1;
            }
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            var (status, text) = _respond(captured, index);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(text) };
        }

        public static string Envelope(string dataJson) => "{\"code\":\"OK\",\"data\":" + dataJson + ",\"message\":\"\"}";
    }

    internal static class Wait
    {
        public static async Task Until(Func<bool> condition, int timeoutMs = 5000, string? what = null)
        {
            var start = DateTime.UtcNow;
            while (!condition())
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException("Tempo esgotado esperando: " + (what ?? "condição"));
                await Task.Delay(10);
            }
        }
    }
}
