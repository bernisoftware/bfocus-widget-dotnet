using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bfocus.Widget
{
    /// <summary>Opções de infraestrutura do <see cref="BFocusWidget"/> (tudo opcional).</summary>
    public sealed class BFocusWidgetOptions
    {
        /// <summary>HttpClient próprio (proxy corporativo, testes). Padrão: um cliente compartilhado.</summary>
        public HttpClient? HttpClient { get; set; }

        /// <summary>Onde gravar <c>last_seen</c> e o token de push. Padrão: <see cref="FileStateStore"/>.</summary>
        public IStateStore? StateStore { get; set; }

        /// <summary>
        /// Thread onde eventos e chamadas ao host acontecem. Padrão: o contexto de quem criou o widget
        /// (a thread de UI no WinForms/WPF/MAUI).
        /// </summary>
        public SynchronizationContext? SynchronizationContext { get; set; }

        /// <summary>Plataforma padrão do <see cref="BFocusWidget.RegisterPushTokenAsync"/>.</summary>
        public string? PushPlatform { get; set; }
    }

    /// <summary>
    /// O widget bFocus num app .NET: identidade, consulta ao launcher-state, badge, pílula de versão,
    /// banner de ciência, ponte com o embed e modo navegador. A UI (WebView) vem de um
    /// <see cref="IBFocusWidgetHost"/> (Bfocus.Widget.WinForms, .Wpf, .Maui); sem host, o
    /// <see cref="Open"/> abre o navegador do sistema.
    /// </summary>
    public sealed class BFocusWidget : IDisposable
    {
        private const string PushTokenKey = "bf:push:token";
        private const string PushPlatformKey = "bf:push:platform";

        private readonly IBFocusWidgetHost? _host;
        private readonly BFocusApiClient _api;
        private readonly IStateStore _store;
        private readonly SynchronizationContext? _sync;
        private readonly string? _pushPlatform;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _apiGate = new SemaphoreSlim(1, 1);

        private BFocusConfig? _config;
        private WidgetIdentity? _identity;
        private int _generation;
        private BadgeModel _badge = new BadgeModel();
        private string _primaryColor = Protocol.BrandFallbackColor;
        private ReleaseNotesState _releaseNotes = ReleaseNotesState.Empty;
        private LauncherState? _lastState;
        private string? _lastLauncherError;
        private TaskCompletionSource<bool> _firstCall = NewTcs(completed: true);
        private CancellationTokenSource? _pollCts;
        private bool _foreground = true;
        private bool _disposed;

        private bool _ticketsOpen;
        private readonly Dictionary<WidgetSurface, SurfaceState> _surfaces = new Dictionary<WidgetSurface, SurfaceState>
        {
            { WidgetSurface.Tickets, new SurfaceState() },
            { WidgetSurface.ReleaseNotesHistory, new SurfaceState() },
            { WidgetSurface.ReleaseNotesBanner, new SurfaceState() },
        };
        private string? _bannerKey;

        public BFocusWidget(IBFocusWidgetHost? host = null, BFocusWidgetOptions? options = null)
        {
            _host = host;
            _api = new BFocusApiClient(options?.HttpClient);
            _store = options?.StateStore ?? new FileStateStore();
            _sync = options?.SynchronizationContext ?? SynchronizationContext.Current;
            _pushPlatform = options?.PushPlatform;
            _host?.Attach(this);
        }

        // ── eventos ─────────────────────────────────────────────────────────────

        /// <summary>O label do badge mudou (<c>''</c>, <c>'•'</c>, <c>'N'</c>, <c>'99+'</c>).</summary>
        public event EventHandler<BadgeChangedEventArgs>? BadgeChanged;

        /// <summary>A pílula de versão mudou.</summary>
        public event EventHandler<ReleaseNotesChangedEventArgs>? ReleaseNotesChanged;

        /// <summary><c>bfocus:error</c> ou 401 de identidade no launcher-state.</summary>
        public event EventHandler<BFocusErrorEventArgs>? Error;

        /// <summary>O widget (chamados) abriu.</summary>
        public event EventHandler? Opened;

        /// <summary>O widget (chamados) fechou.</summary>
        public event EventHandler? Closed;

        /// <summary>A cor do tenant chegou ou mudou (pinte o botão).</summary>
        public event EventHandler<PrimaryColorChangedEventArgs>? PrimaryColorChanged;

        // ── estado público ──────────────────────────────────────────────────────

        public string BadgeLabel { get { lock (_lock) return _badge.Label; } }
        public ReleaseNotesState ReleaseNotes { get { lock (_lock) return _releaseNotes; } }
        public string PrimaryColor { get { lock (_lock) return _primaryColor; } }
        public bool IsOpen { get { lock (_lock) return _ticketsOpen; } }
        public bool IsInitialized { get { lock (_lock) return _identity != null; } }
        public WidgetIdentity? Identity { get { lock (_lock) return _identity; } }
        public LauncherState? LastLauncherState { get { lock (_lock) return _lastState; } }

        /// <summary>Textos do idioma atual (para os componentes de UI).</summary>
        public WidgetStrings Strings { get { lock (_lock) return WidgetStrings.For(_identity?.Locale ?? LocaleResolver.Resolve(null)); } }

        /// <summary><c>true</c> quando não há WebView e o <see cref="Open"/> vai para o navegador.</summary>
        public bool IsBrowserMode => _host == null || !_host.IsWebViewAvailable;

        /// <summary>Ajuste de teste: intervalo real da consulta (sem o piso de 5 s).</summary>
        internal TimeSpan? PollIntervalOverride { get; set; }

        internal BFocusApiClient Api => _api;

        // ── init / identidade ───────────────────────────────────────────────────

        /// <summary>
        /// Inicializa (ou troca) a identidade e faz a primeira consulta ao launcher-state. Termina
        /// depois dela; nada mais do widget chama o bFocus em paralelo com essa primeira chamada.
        /// </summary>
        public async Task InitAsync(BFocusConfig config, CancellationToken cancellationToken = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            ThrowIfDisposed();
            config.Validate();

            string? hash = config.UserHash;
            if (config.UserHashProvider != null)
            {
                var provided = await CallHashProviderAsync(config, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(provided)) hash = provided;
            }

            var identity = WidgetIdentity.Create(config, Protocol.Client, hash);
            var fx = new Effects(this);
            TaskCompletionSource<bool> first;
            int generation;
            lock (_lock)
            {
                StopPollingLocked();
                var previous = _identity;
                var sameUrl = previous != null && EmbedUrl.Build(previous) == EmbedUrl.Build(identity);
                if (previous != null && !sameUrl)
                {
                    // Outra pessoa (ou outro payload): a WebView é recriada (§4.3).
                    if (_ticketsOpen) CloseLocked(fx, refresh: false);
                    DestroyAllSurfacesLocked(fx);
                }
                _config = config;
                _identity = identity;
                generation = ++_generation;
                _lastLauncherError = null;
                if (previous == null || !previous.IsSamePerson(identity))
                {
                    var oldLabel = _badge.Label;
                    _badge = new BadgeModel(_store.Get(identity.LastSeenStorageKey));
                    if (oldLabel != _badge.Label) fx.Badge(_badge.Label);
                    _lastState = null;
                }
                _firstCall = first = NewTcs(completed: false);
            }
            fx.Run();

            try
            {
                await RefreshCoreAsync(generation, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                first.TrySetResult(true);
            }

            var token = _store.Get(PushTokenKey);
            if (!string.IsNullOrEmpty(token))
            {
                // Identidade (possivelmente) nova: registra de novo o token guardado.
                await RegisterPushCoreAsync(token!, _store.Get(PushPlatformKey), cancellationToken).ConfigureAwait(false);
            }

            lock (_lock)
            {
                if (generation == _generation) StartPollingLocked();
            }
        }

        /// <summary>Versão "dispara e esquece" do <see cref="InitAsync"/>; falhas vão para <see cref="Error"/>.</summary>
        public void Init(BFocusConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate(); // erro de configuração é do programador: lança já
            Forget(InitAsync(config));
        }

        // ── abrir / fechar ──────────────────────────────────────────────────────

        /// <summary>
        /// Abre o widget. <paramref name="target"/>: lista (padrão), <see cref="WidgetTarget.New"/>,
        /// <see cref="WidgetTarget.Chat"/> ou <see cref="WidgetTarget.Ticket"/>. Sem WebView, abre o
        /// navegador do sistema (modo navegador).
        /// </summary>
        public void Open(WidgetTarget? target = null)
        {
            ThrowIfDisposed();
            var identity = RequireIdentity();
            if (IsBrowserMode)
            {
                OpenExternalCore(EmbedUrl.Build(identity, EmbedPage.Tickets, HostMode.Browser, target?.ToOpenParam()));
                return;
            }
            Forget(OpenCoreAsync(target));
        }

        private async Task OpenCoreAsync(WidgetTarget? target)
        {
            // O embed também chama a API: espera a primeira chamada (que cria o usuário) terminar.
            Task first;
            lock (_lock) first = _firstCall.Task;
            await first.ConfigureAwait(false);

            var fx = new Effects(this);
            lock (_lock)
            {
                var identity = _identity;
                if (identity == null) return;
                var s = _surfaces[WidgetSurface.Tickets];
                var wasOpen = _ticketsOpen;
                string url;
                bool forceLoad;
                if (!s.Loaded)
                {
                    url = EmbedUrl.Build(identity, EmbedPage.Tickets, HostMode.Native, target?.ToOpenParam());
                    forceLoad = true;
                    s.Loaded = true;
                    s.Ready = false;
                    s.Url = url;
                    s.PendingNavigate = null;
                }
                else
                {
                    url = s.Url!;
                    forceLoad = false;
                    if (target != null)
                    {
                        // Já carregada: deep link vira bfocus:navigate (ou espera o ready).
                        if (s.Ready) fx.Script(WidgetSurface.Tickets, HostScript.Receive(MessageTypes.Navigate, target.ToNavigatePayloadJson()));
                        else s.PendingNavigate = target;
                    }
                }
                _ticketsOpen = true;
                _badge.Open();
                StopPollingLocked(); // aberto: o badge segue o bfocus:unread
                fx.Host(h => h.ShowSurface(WidgetSurface.Tickets, url, forceLoad));
                if (s.Ready) fx.Script(WidgetSurface.Tickets, HostScript.Receive(MessageTypes.Open));
                if (!wasOpen) fx.Opened();
            }
            fx.Run();
        }

        /// <summary>Fecha o widget, mantendo a WebView para reabrir rápido, e consulta o launcher-state na hora.</summary>
        public void Close()
        {
            var fx = new Effects(this);
            lock (_lock) CloseLocked(fx, refresh: true);
            fx.Run();
        }

        private void CloseLocked(Effects fx, bool refresh)
        {
            if (!_ticketsOpen) return;
            _ticketsOpen = false;
            _badge.Close();
            fx.Script(WidgetSurface.Tickets, HostScript.Receive(MessageTypes.Close));
            fx.Host(h => h.HideSurface(WidgetSurface.Tickets));
            fx.Closed();
            if (refresh && _identity != null)
            {
                var generation = _generation;
                fx.Later(() => RefreshThenPollAsync(generation));
            }
        }

        /// <summary>Abre o histórico de versões (<c>release-notes.html#view=history</c>).</summary>
        public void OpenReleaseNotesHistory()
        {
            ThrowIfDisposed();
            var identity = RequireIdentity();
            if (IsBrowserMode)
            {
                OpenExternalCore(EmbedUrl.Build(identity, EmbedPage.ReleaseNotes, HostMode.Browser, view: "history"));
                return;
            }
            var fx = new Effects(this);
            lock (_lock)
            {
                var s = _surfaces[WidgetSurface.ReleaseNotesHistory];
                var forceLoad = !s.Loaded;
                if (forceLoad)
                {
                    s.Loaded = true;
                    s.Ready = false;
                    s.Url = EmbedUrl.Build(identity, EmbedPage.ReleaseNotes, HostMode.Native, view: "history");
                }
                s.Visible = true;
                var url = s.Url!;
                fx.Host(h => h.ShowSurface(WidgetSurface.ReleaseNotesHistory, url, forceLoad));
            }
            fx.Run();
        }

        /// <summary>Fecha o histórico (mantém a WebView).</summary>
        public void CloseReleaseNotesHistory()
        {
            var fx = new Effects(this);
            lock (_lock)
            {
                var s = _surfaces[WidgetSurface.ReleaseNotesHistory];
                if (!s.Visible) return;
                s.Visible = false;
                fx.Host(h => h.HideSurface(WidgetSurface.ReleaseNotesHistory));
            }
            fx.Run();
        }

        // ── launcher-state ──────────────────────────────────────────────────────

        /// <summary>Consulta o launcher-state agora.</summary>
        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            int generation;
            lock (_lock)
            {
                if (_identity == null) return Task.CompletedTask;
                generation = _generation;
            }
            return RefreshCoreAsync(generation, cancellationToken);
        }

        /// <summary>Versão "dispara e esquece" do <see cref="RefreshAsync"/>.</summary>
        public void Refresh() => Forget(RefreshAsync());

        private async Task RefreshThenPollAsync(int generation)
        {
            await RefreshCoreAsync(generation, CancellationToken.None).ConfigureAwait(false);
            lock (_lock)
            {
                if (generation != _generation) return;
                StopPollingLocked(); // recomeça o intervalo a partir de agora
                StartPollingLocked();
            }
        }

        private async Task RefreshCoreAsync(int generation, CancellationToken ct)
        {
            await _apiGate.WaitAsync(ct).ConfigureAwait(false); // chamadas ao bFocus em fila, nunca em paralelo
            try
            {
                WidgetIdentity? identity;
                lock (_lock)
                {
                    if (generation != _generation) return;
                    identity = _identity;
                }
                if (identity == null) return;

                var result = await _api.FetchLauncherStateAsync(identity, ct).ConfigureAwait(false);
                if (result.Outcome == ApiOutcome.IdentityError)
                {
                    BFocusConfig? config;
                    lock (_lock) config = _config;
                    if (config?.UserHashProvider != null)
                    {
                        // Hash vencido/errado: pede outro UMA vez e repete a chamada. O onError só sai
                        // se a repetição também for recusada (ou se não houver hash novo).
                        var renewed = await RenewUserHashAsync(generation, ct).ConfigureAwait(false);
                        if (renewed != null) result = await _api.FetchLauncherStateAsync(renewed, ct).ConfigureAwait(false);
                    }
                }
                switch (result.Outcome)
                {
                    case ApiOutcome.Ok:
                        if (result.State != null) ApplyState(generation, result.State);
                        break;
                    case ApiOutcome.IdentityError:
                        ReportLauncherError(result.ErrorCode!);
                        break;
                    case ApiOutcome.HttpError:
                        // Outros 4xx (origem não cadastrada, chave errada…) também avisam o integrador.
                        ReportLauncherError(string.IsNullOrEmpty(result.ErrorCode) ? "HTTP_" + result.StatusCode : result.ErrorCode!);
                        break;
                    default:
                        break; // rede/5xx: silêncio, tenta de novo no próximo ciclo
                }
            }
            finally
            {
                _apiGate.Release();
            }
        }

        private void ReportLauncherError(string code)
        {
            var fx = new Effects(this);
            lock (_lock)
            {
                // Um aviso por código: não repete o mesmo erro a cada ciclo (volta a avisar após um sucesso).
                if (_lastLauncherError == code) return;
                _lastLauncherError = code;
                fx.Error(code, null);
            }
            fx.Run();
        }

        private void ApplyState(int generation, LauncherState state)
        {
            var fx = new Effects(this);
            lock (_lock)
            {
                if (generation != _generation || _identity == null) return;
                _lastState = state;
                _lastLauncherError = null;

                var color = ReleaseNotesState.FirstNonEmpty(state.PrimaryColor) ?? Protocol.BrandFallbackColor;
                if (color != _primaryColor)
                {
                    _primaryColor = color;
                    fx.Color(color);
                }

                var oldLabel = _badge.Label;
                var oldSeen = _badge.LastSeen;
                _badge.ApplyState(state.Tickets.LatestEventAt);
                if (_badge.LastSeen != oldSeen) _store.Set(_identity.LastSeenStorageKey, _badge.LastSeen);
                if (_badge.Label != oldLabel)
                {
                    fx.Badge(_badge.Label);
                    if (_badge.Label == BadgeModel.Dot) NotifyDotLocked(fx);
                }

                SetReleaseNotesLocked(fx, ReleaseNotesState.From(state.ReleaseNotes, state.PrimaryColor));
                MaybeShowBannerLocked(fx, _releaseNotes.BannerIds);
            }
            fx.Run();
        }

        private void NotifyDotLocked(Effects fx)
        {
            if (_config == null || !_config.Notifications || _host == null) return;
            var strings = WidgetStrings.For(_identity?.Locale);
            fx.Host(h => h.ShowNotification(strings.NotificationTitle, strings.NotificationBody, () => Open()));
        }

        private void SetReleaseNotesLocked(Effects fx, ReleaseNotesState state)
        {
            if (state.Equals(_releaseNotes)) return;
            _releaseNotes = state;
            fx.ReleaseNotes(state);
        }

        private void MaybeShowBannerLocked(Effects fx, IReadOnlyList<string> ids)
        {
            if (_config == null || !_config.AutoShowReleaseBanner || ids.Count == 0 || _identity == null) return;
            // Modo navegador: o banner exige modal; fica o ponto na pílula e o histórico no navegador.
            if (IsBrowserMode) return;
            var key = string.Join(",", ids);
            var s = _surfaces[WidgetSurface.ReleaseNotesBanner];
            if (s.Visible && _bannerKey == key) return; // não reabre a mesma fila enquanto aberta
            _bannerKey = key;
            s.Loaded = true;
            s.Visible = true;
            s.Ready = false;
            s.Url = EmbedUrl.Build(_identity, EmbedPage.ReleaseNotes, HostMode.Native, view: "banner", ids: ids);
            var url = s.Url;
            fx.Host(h => h.ShowSurface(WidgetSurface.ReleaseNotesBanner, url, true));
        }

        // ── ponte embed → pacote ────────────────────────────────────────────────

        /// <summary>
        /// Entrada da ponte: o host chama com a string JSON recebida da WebView e a URL do documento que
        /// a mandou (WebView2: <c>args.Source</c>). Mensagens de outra origem ou de tipo desconhecido são
        /// ignoradas.
        /// </summary>
        public void HandleEmbedMessage(WidgetSurface surface, string? sourceUrl, string json)
        {
            WidgetIdentity? identity;
            lock (_lock) identity = _identity;
            if (identity == null) return;
            if (sourceUrl != null && !identity.Origin.IsSameOrigin(sourceUrl)) return; // defesa em profundidade
            if (!EmbedMessage.TryParse(json, out var msg) || msg == null) return;

            var fx = new Effects(this);
            lock (_lock)
            {
                if (_identity == null) return;
                var s = _surfaces[surface];
                switch (msg.Type)
                {
                    case MessageTypes.Ready:
                        s.Ready = true;
                        fx.Host(h => h.MarkReady(surface));
                        var hp = msg.GetInt("hp");
                        if (hp.HasValue && hp.Value != Protocol.HostProtocol)
                            fx.Error(Protocol.ErrorHostProtocolMismatch, "hp=" + hp.Value);
                        if (surface == WidgetSurface.Tickets)
                        {
                            if (_ticketsOpen) fx.Script(surface, HostScript.Receive(MessageTypes.Open));
                            if (s.PendingNavigate != null)
                            {
                                fx.Script(surface, HostScript.Receive(MessageTypes.Navigate, s.PendingNavigate.ToNavigatePayloadJson()));
                                s.PendingNavigate = null;
                            }
                        }
                        break;

                    case MessageTypes.Close:
                        if (surface == WidgetSurface.Tickets) CloseLocked(fx, refresh: true);
                        else if (surface == WidgetSurface.ReleaseNotesHistory) HideHistoryLocked(fx);
                        break; // o banner nunca fecha por pedido do usuário

                    case MessageTypes.Unread:
                    {
                        var old = _badge.Label;
                        _badge.ApplyUnread(msg.GetInt("count") ?? 0);
                        if (_badge.Label != old) fx.Badge(_badge.Label);
                        break;
                    }

                    case MessageTypes.Seen:
                    {
                        var old = _badge.LastSeen;
                        _badge.ApplySeen(msg.GetString("latest_event_at"));
                        if (_badge.LastSeen != old) _store.Set(_identity.LastSeenStorageKey, _badge.LastSeen);
                        break;
                    }

                    case MessageTypes.Branding:
                    {
                        var color = ReleaseNotesState.FirstNonEmpty(msg.GetString("primary_color"));
                        if (color != null && color != _primaryColor)
                        {
                            _primaryColor = color;
                            fx.Color(color);
                        }
                        break;
                    }

                    case MessageTypes.RnBranding:
                    {
                        var color = ReleaseNotesState.FirstNonEmpty(msg.GetString("primary_color"));
                        if (color != null) SetReleaseNotesLocked(fx, _releaseNotes.WithColor(color));
                        break;
                    }

                    case MessageTypes.Error:
                    {
                        var code = msg.GetString("code") ?? "WIDGET_ERROR";
                        fx.Error(code, msg.GetString("detail"));
                        if (code == Protocol.ErrorUserHashInvalid && _config?.UserHashProvider != null)
                        {
                            var generation = _generation;
                            fx.Later(() => RenewHashAndReloadAsync(generation));
                        }
                        break;
                    }

                    case MessageTypes.OpenExternal:
                    {
                        var url = msg.GetString("url");
                        if (EmbedOrigin.IsExternalSafe(url)) fx.External(url!);
                        break;
                    }

                    case MessageTypes.Download:
                    {
                        var url = msg.GetString("url");
                        if (EmbedOrigin.IsDownloadSafe(url))
                        {
                            var name = FileNames.Sanitize(msg.GetString("filename"));
                            fx.Host(h => h.Download(url!, name));
                        }
                        break;
                    }

                    case MessageTypes.RnDone:
                    {
                        // Fila do banner esgotada: fecha e reconsulta o estado.
                        var banner = _surfaces[WidgetSurface.ReleaseNotesBanner];
                        banner.Reset();
                        _bannerKey = null;
                        fx.Host(h => h.DestroySurface(WidgetSurface.ReleaseNotesBanner));
                        var generation = _generation;
                        fx.Later(() => RefreshCoreAsync(generation, CancellationToken.None));
                        break;
                    }

                    case MessageTypes.RnCloseHistory:
                        HideHistoryLocked(fx);
                        break;

                    default:
                        break; // tipo desconhecido: ignora (o protocolo só cresce)
                }
            }
            fx.Run();
        }

        private void HideHistoryLocked(Effects fx)
        {
            var s = _surfaces[WidgetSurface.ReleaseNotesHistory];
            if (!s.Visible) return;
            s.Visible = false;
            fx.Host(h => h.HideSurface(WidgetSurface.ReleaseNotesHistory));
        }

        /// <summary>O usuário fechou a janela da tela pelo sistema (✕ da janela, voltar). O banner não fecha.</summary>
        public void NotifySurfaceClosedByUser(WidgetSurface surface)
        {
            if (surface == WidgetSurface.Tickets) Close();
            else if (surface == WidgetSurface.ReleaseNotesHistory) CloseReleaseNotesHistory();
        }

        /// <summary>A WebView da tela foi descartada pelo host (ex.: processo do navegador caiu): recarrega na próxima abertura.</summary>
        public void NotifySurfaceLost(WidgetSurface surface)
        {
            lock (_lock)
            {
                var s = _surfaces[surface];
                s.Loaded = false;
                s.Ready = false;
            }
        }

        /// <summary>O host navegou/recarregou a tela sozinho ("tentar de novo"): espera um novo ready.</summary>
        public void NotifySurfaceReloading(WidgetSurface surface)
        {
            lock (_lock) _surfaces[surface].Ready = false;
        }

        /// <summary>Navegação para fora da origem do embed: o host cancela e chama isto.</summary>
        public void OpenExternalFromHost(string url)
        {
            if (EmbedOrigin.IsExternalSafe(url)) OpenExternalCore(url);
        }

        private void OpenExternalCore(string url)
        {
            var fx = new Effects(this);
            fx.External(url);
            fx.Run();
        }

        // ── userHash ────────────────────────────────────────────────────────────

        private async Task<string?> CallHashProviderAsync(BFocusConfig config, CancellationToken ct)
        {
            try
            {
                return await config.UserHashProvider!(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                RaiseError(Protocol.ErrorUserHashProviderFailed, e.Message);
                return null;
            }
        }

        /// <summary>Pede um hash novo e troca a identidade. <c>null</c> se não mudou nada.</summary>
        private async Task<WidgetIdentity?> RenewUserHashAsync(int generation, CancellationToken ct)
        {
            BFocusConfig? config;
            WidgetIdentity? current;
            lock (_lock)
            {
                config = _config;
                current = _identity;
            }
            if (config == null || current == null) return null;
            var hash = await CallHashProviderAsync(config, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(hash) || hash == current.UserHash) return null; // mesmo hash: não adianta repetir
            lock (_lock)
            {
                if (generation != _generation || _identity != current) return null;
                _identity = current.WithUserHash(config, hash);
                return _identity;
            }
        }

        private async Task RenewHashAndReloadAsync(int generation)
        {
            var renewed = await RenewUserHashAsync(generation, CancellationToken.None).ConfigureAwait(false);
            if (renewed == null) return;
            var fx = new Effects(this);
            lock (_lock)
            {
                if (generation != _generation) return;
                foreach (var pair in _surfaces)
                {
                    var surface = pair.Key;
                    var s = pair.Value;
                    if (!s.Loaded) continue;
                    var visible = surface == WidgetSurface.Tickets ? _ticketsOpen : s.Visible;
                    if (!visible)
                    {
                        s.Reset();
                        fx.Host(h => h.DestroySurface(surface));
                        continue;
                    }
                    // Visível: recarrega já com o hash novo.
                    s.Ready = false;
                    s.Url = surface == WidgetSurface.Tickets
                        ? EmbedUrl.Build(renewed, EmbedPage.Tickets)
                        : surface == WidgetSurface.ReleaseNotesHistory
                            ? EmbedUrl.Build(renewed, EmbedPage.ReleaseNotes, view: "history")
                            : EmbedUrl.Build(renewed, EmbedPage.ReleaseNotes, view: "banner", ids: _bannerKey?.Split(','));
                    var url = s.Url;
                    fx.Host(h => h.ShowSurface(surface, url, true));
                }
            }
            fx.Run();
        }

        // ── push (mobile) ───────────────────────────────────────────────────────

        /// <summary>
        /// Registra o token FCM do aparelho (<c>POST /widget/push/devices</c>). O token fica guardado e é
        /// registrado de novo a cada <see cref="InitAsync"/> (identidade nova); o <see cref="LogoutAsync"/>
        /// cancela o registro.
        /// </summary>
        public async Task RegisterPushTokenAsync(string token, string? platform = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token vazio.", nameof(token));
            _store.Set(PushTokenKey, token);
            _store.Set(PushPlatformKey, platform);
            Task first;
            lock (_lock)
            {
                if (_identity == null) return; // registra no próximo init
                first = _firstCall.Task;
            }
            await first.ConfigureAwait(false); // espera a 1ª chamada (que cria o usuário)
            await RegisterPushCoreAsync(token, platform, cancellationToken).ConfigureAwait(false);
        }

        private async Task RegisterPushCoreAsync(string token, string? platform, CancellationToken ct)
        {
            await _apiGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                WidgetIdentity? identity;
                lock (_lock) identity = _identity;
                if (identity == null) return;
                await _api.RegisterPushAsync(identity, token, platform ?? _pushPlatform ?? DefaultPushPlatform(), ct).ConfigureAwait(false);
            }
            finally
            {
                _apiGate.Release();
            }
        }

        /// <summary>
        /// Trata o <c>data</c> de um push. <c>true</c> se é do bFocus; nesse caso abre o widget no item certo.
        /// </summary>
        public bool HandlePush(IEnumerable<KeyValuePair<string, string>> data)
        {
            var result = PushMessage.Handle(data);
            if (result.Handled && IsInitialized) Open(result.Navigate);
            return result.Handled;
        }

        /// <inheritdoc cref="HandlePush(IEnumerable{KeyValuePair{string, string}})"/>
        public bool HandlePush(IEnumerable<KeyValuePair<string, object>> data)
        {
            var result = PushMessage.Handle(data);
            if (result.Handled && IsInitialized) Open(result.Navigate);
            return result.Handled;
        }

        internal static string DefaultPushPlatform()
        {
#if NET5_0_OR_GREATER
            if (OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsMacOS()) return "ios";
#endif
            return "android";
        }

        // ── logout ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Para a consulta, cancela o registro de push, limpa o estado local desta identidade e os dados
        /// da WebView da origem do embed. Depois disso, só um novo <see cref="InitAsync"/>.
        /// </summary>
        public async Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            WidgetIdentity? identity;
            var fx = new Effects(this);
            lock (_lock)
            {
                identity = _identity;
                StopPollingLocked();
                if (_ticketsOpen) CloseLocked(fx, refresh: false);
                DestroyAllSurfacesLocked(fx);
                _identity = null;
                _config = null;
                _generation++;
                _lastState = null;
                _lastLauncherError = null;
                if (_badge.Label.Length > 0) fx.Badge(string.Empty);
                _badge = new BadgeModel();
                SetReleaseNotesLocked(fx, ReleaseNotesState.Empty);
                if (_primaryColor != Protocol.BrandFallbackColor)
                {
                    _primaryColor = Protocol.BrandFallbackColor;
                    fx.Color(_primaryColor);
                }
                if (identity != null) fx.Host(h => h.ClearWebViewData(identity));
            }
            fx.Run();

            if (identity == null) return;
            _store.Set(identity.LastSeenStorageKey, null);
            var token = _store.Get(PushTokenKey);
            if (!string.IsNullOrEmpty(token))
            {
                await _apiGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await _api.UnregisterPushAsync(identity, token!, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _apiGate.Release();
                }
                _store.Set(PushTokenKey, null);
                _store.Set(PushPlatformKey, null);
            }
        }

        /// <summary>Versão "dispara e esquece" do <see cref="LogoutAsync"/>.</summary>
        public void Logout() => Forget(LogoutAsync());

        // ── ciclo de vida ───────────────────────────────────────────────────────

        /// <summary>
        /// Mobile: o app foi para o fundo (<c>false</c>) ou voltou (<c>true</c>). A consulta só roda em
        /// primeiro plano. Desktop não precisa chamar (o "•" e a notificação servem justamente para
        /// quando o usuário está em outra janela).
        /// </summary>
        public void SetAppForeground(bool foreground)
        {
            int generation;
            lock (_lock)
            {
                if (_foreground == foreground) return;
                _foreground = foreground;
                if (!foreground)
                {
                    StopPollingLocked();
                    return;
                }
                if (_identity == null || _ticketsOpen) return;
                generation = _generation;
            }
            Forget(RefreshThenPollAsync(generation));
        }

        private void StartPollingLocked()
        {
            if (_pollCts != null || _disposed || !_foreground || _ticketsOpen || _identity == null) return;
            var cts = new CancellationTokenSource();
            _pollCts = cts;
            var interval = PollIntervalOverride ?? TimeSpan.FromSeconds(Math.Max(5, _config?.PollIntervalSeconds ?? Protocol.DefaultPollIntervalSeconds));
            var generation = _generation;
            Forget(PollLoopAsync(generation, interval, cts.Token));
        }

        private void StopPollingLocked()
        {
            var cts = _pollCts;
            _pollCts = null;
            if (cts == null) return;
            cts.Cancel();
            cts.Dispose();
        }

        private async Task PollLoopAsync(int generation, TimeSpan interval, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    await RefreshCoreAsync(generation, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // parada normal (abriu, fechou o app, logout)
            }
        }

        private void DestroyAllSurfacesLocked(Effects fx)
        {
            foreach (var pair in _surfaces)
            {
                var surface = pair.Key;
                if (!pair.Value.Loaded && !pair.Value.Visible) continue;
                pair.Value.Reset();
                fx.Host(h => h.DestroySurface(surface));
            }
            _bannerKey = null;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                StopPollingLocked();
            }
        }

        // ── utilidades ──────────────────────────────────────────────────────────

        private WidgetIdentity RequireIdentity()
        {
            lock (_lock)
            {
                return _identity ?? throw new InvalidOperationException("Chame Init/InitAsync antes de usar o widget.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BFocusWidget));
        }

        private void RaiseError(string code, string? detail)
        {
            var fx = new Effects(this);
            fx.Error(code, detail);
            fx.Run();
        }

        private void Forget(Task task)
        {
            task.ContinueWith(
                t => RaiseError("WIDGET_INTERNAL_ERROR", t.Exception?.GetBaseException().Message),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static TaskCompletionSource<bool> NewTcs(bool completed)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (completed) tcs.SetResult(true);
            return tcs;
        }

        /// <summary>Roda na thread de UI (contexto capturado) ou direto quando não há contexto.</summary>
        private void OnUi(Action action)
        {
            if (_sync == null || SynchronizationContext.Current == _sync) action();
            else _sync.Post(_ => action(), null);
        }

        private sealed class SurfaceState
        {
            public bool Loaded;
            public bool Ready;
            public bool Visible;
            public string? Url;
            public WidgetTarget? PendingNavigate;

            public void Reset()
            {
                Loaded = false;
                Ready = false;
                Visible = false;
                Url = null;
                PendingNavigate = null;
            }
        }

        /// <summary>
        /// Efeitos acumulados dentro do lock e executados depois dele, na ordem: o host e os eventos do
        /// integrador nunca rodam segurando o lock do widget.
        /// </summary>
        private sealed class Effects
        {
            private readonly BFocusWidget _w;
            private readonly List<Action> _ui = new List<Action>();
            private readonly List<Func<Task>> _later = new List<Func<Task>>();

            public Effects(BFocusWidget w) { _w = w; }

            public void Host(Action<IBFocusWidgetHost> a)
            {
                var host = _w._host;
                if (host != null) _ui.Add(() => a(host));
            }

            public void Script(WidgetSurface s, string script) => Host(h => h.ExecuteScript(s, script));

            public void External(string url)
            {
                var host = _w._host;
                if (host != null) _ui.Add(() => host.OpenExternal(url));
                else _ui.Add(() => SystemBrowser.Open(url));
            }

            public void Badge(string label) => _ui.Add(() => _w.BadgeChanged?.Invoke(_w, new BadgeChangedEventArgs(label)));
            public void ReleaseNotes(ReleaseNotesState s) => _ui.Add(() => _w.ReleaseNotesChanged?.Invoke(_w, new ReleaseNotesChangedEventArgs(s)));
            public void Color(string c) => _ui.Add(() => _w.PrimaryColorChanged?.Invoke(_w, new PrimaryColorChangedEventArgs(c)));
            public void Error(string code, string? detail) => _ui.Add(() => _w.Error?.Invoke(_w, new BFocusErrorEventArgs(code, detail)));
            public void Opened() => _ui.Add(() => _w.Opened?.Invoke(_w, EventArgs.Empty));
            public void Closed() => _ui.Add(() => _w.Closed?.Invoke(_w, EventArgs.Empty));

            /// <summary>Trabalho assíncrono disparado depois dos efeitos de UI.</summary>
            public void Later(Func<Task> work) => _later.Add(work);

            public void Run()
            {
                if (_ui.Count > 0)
                {
                    var actions = _ui.ToArray();
                    _w.OnUi(() =>
                    {
                        foreach (var a in actions)
                        {
                            try { a(); }
                            catch (Exception e) when (!(e is OutOfMemoryException))
                            {
                                // Handler do integrador que lança não pode quebrar a ponte.
                                System.Diagnostics.Debug.WriteLine("[bfocus] " + e);
                            }
                        }
                    });
                }
                foreach (var work in _later) _w.Forget(Task.Run(work));
            }
        }
    }
}
