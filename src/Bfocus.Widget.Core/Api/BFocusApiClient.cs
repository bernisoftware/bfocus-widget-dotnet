using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bfocus.Widget
{
    /// <summary>Desfecho de uma chamada ao bFocus.</summary>
    public enum ApiOutcome
    {
        /// <summary>2xx com envelope válido.</summary>
        Ok,
        /// <summary>Rede, tempo esgotado, 5xx, 408 ou 429: ignorado em silêncio, tenta no próximo ciclo.</summary>
        Transient,
        /// <summary>401 <c>WIDGET_USER_HASH_INVALID</c> ou <c>WIDGET_VERIFIED_SESSION_REQUIRED</c>.</summary>
        IdentityError,
        /// <summary>Outro 4xx (chave, origem, widget desligado…).</summary>
        HttpError,
    }

    /// <summary>Resultado do launcher-state (ou do push, sem <see cref="State"/>).</summary>
    public sealed class ApiResult
    {
        internal ApiResult(ApiOutcome outcome, int statusCode, string? errorCode, LauncherState? state)
        {
            Outcome = outcome;
            StatusCode = statusCode;
            ErrorCode = errorCode;
            State = state;
        }

        public ApiOutcome Outcome { get; }
        /// <summary>Status HTTP (0 sem resposta).</summary>
        public int StatusCode { get; }
        /// <summary>Código de máquina do envelope (<c>message</c>/<c>detail</c>).</summary>
        public string? ErrorCode { get; }
        public LauncherState? State { get; }
        public bool IsOk => Outcome == ApiOutcome.Ok;
    }

    /// <summary>
    /// Cliente REST mínimo do widget: launcher-state e push. O <see cref="HttpClient"/> é
    /// injetável (testes, proxy corporativo); o tempo máximo de 15 s é aplicado por chamada.
    /// </summary>
    public sealed class BFocusApiClient
    {
        private static readonly Lazy<HttpClient> SharedClient = new Lazy<HttpClient>(CreateDefaultClient);
        private readonly HttpClient _http;

        public BFocusApiClient(HttpClient? httpClient = null)
        {
            _http = httpClient ?? SharedClient.Value;
        }

        /// <summary>Tempo máximo por chamada (15 s pelo contrato; ajustável só nos testes).</summary>
        internal TimeSpan Timeout { get; set; } = Protocol.RequestTimeout;

        public Task<ApiResult> FetchLauncherStateAsync(WidgetIdentity identity, CancellationToken ct = default) =>
            SendAsync(HttpRequestSpec.LauncherState(identity), parseState: true, ct);

        public Task<ApiResult> RegisterPushAsync(WidgetIdentity identity, string token, string platform, CancellationToken ct = default) =>
            SendAsync(HttpRequestSpec.PushRegister(identity, token, platform), parseState: false, ct);

        public Task<ApiResult> UnregisterPushAsync(WidgetIdentity identity, string token, CancellationToken ct = default) =>
            SendAsync(HttpRequestSpec.PushUnregister(identity, token), parseState: false, ct);

        private async Task<ApiResult> SendAsync(HttpRequestSpec spec, bool parseState, CancellationToken ct)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            using (var request = spec.ToHttpRequestMessage())
            {
                cts.CancelAfter(Timeout);
                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return new ApiResult(ApiOutcome.Transient, 0, "TIMEOUT", null); // estourou os 15 s
                }
                catch (HttpRequestException)
                {
                    return new ApiResult(ApiOutcome.Transient, 0, "NETWORK", null);
                }

                using (response)
                {
                    var status = (int)response.StatusCode;
                    string body;
                    try
                    {
                        body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException && !ct.IsCancellationRequested)
                    {
                        return new ApiResult(ApiOutcome.Transient, status, "NETWORK", null);
                    }
                    return Interpret(status, body, parseState);
                }
            }
        }

        /// <summary>Interpreta status + envelope <c>{code, data, message}</c>.</summary>
        internal static ApiResult Interpret(int status, string body, bool parseState)
        {
            if (status >= 200 && status < 300)
            {
                if (!parseState) return new ApiResult(ApiOutcome.Ok, status, null, null);
                try
                {
                    using (var doc = JsonDocument.Parse(body))
                    {
                        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                            doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                            return new ApiResult(ApiOutcome.Ok, status, null, LauncherState.FromJson(data));
                    }
                }
                catch (JsonException) { /* corpo inesperado: trata como falha passageira */ }
                return new ApiResult(ApiOutcome.Transient, status, "MALFORMED", null);
            }

            if (status >= 500 || status == 408 || status == 429)
                return new ApiResult(ApiOutcome.Transient, status, ExtractCode(body), null);

            var code = ExtractCode(body);
            if (status == 401 && IsIdentityError(code))
                return new ApiResult(ApiOutcome.IdentityError, status, code, null);
            return new ApiResult(ApiOutcome.HttpError, status, code, null);
        }

        public static bool IsIdentityError(string? code) =>
            code == Protocol.ErrorUserHashInvalid || code == Protocol.ErrorVerifiedSessionRequired;

        /// <summary>Código de máquina: <c>message</c>, ou <c>detail</c> (string ou <c>{code}</c>).</summary>
        internal static string? ExtractCode(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return null;
                    var message = Json.GetString(root, "message");
                    if (!string.IsNullOrEmpty(message)) return message;
                    if (root.TryGetProperty("detail", out var detail))
                    {
                        if (detail.ValueKind == JsonValueKind.String) return detail.GetString();
                        if (detail.ValueKind == JsonValueKind.Object)
                            return Json.GetString(detail, "code") ?? Json.GetString(detail, "message");
                    }
                }
            }
            catch (JsonException) { /* corpo não-JSON (proxy, HTML de erro) */ }
            return null;
        }

        private static HttpClient CreateDefaultClient()
        {
#if NET5_0_OR_GREATER
            // Recicla conexões para acompanhar troca de DNS do bFocus (app desktop fica aberto dias).
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
            return new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
#else
            return new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
#endif
        }
    }
}
