# bFocus Widget para .NET

> **English summary.** Native .NET packages for the bFocus support widget (tickets, live chat and
> release notes). They show the same web embed from the bFocus CDN inside a WebView and handle only
> what is native: the floating button with badge, the version pill, the release-notes banner,
> downloads, system notifications, push (MAUI) and a browser fallback when no WebView is available.
> Packages: `Bfocus.Widget.Core` (netstandard2.0 / net8.0), `Bfocus.Widget.WinForms` and
> `Bfocus.Widget.Wpf` (net8.0-windows, WebView2) and `Bfocus.Widget.Maui` (net10.0 Android, iOS,
> Mac Catalyst, Windows). Only the public key `bf_pk_…` and a `userHash` computed **on your server**
> go into the app. MIT licensed.

Pacotes nativos do widget bFocus: chamados, chat ao vivo e release notes dentro do seu app .NET.
A tela é a mesma do widget web (o `embed.html` da CDN numa WebView). O pacote cuida só do que é
nativo: botão com badge, pílula de versão, banner de ciência, downloads, notificação do sistema,
push e o modo navegador quando não há WebView.

| Pacote | Alvos | Para |
|---|---|---|
| `Bfocus.Widget.Core` | netstandard2.0, net8.0 | a lógica (sem UI): identidade, launcher-state, badge, pílula, push, modo navegador |
| `Bfocus.Widget.WinForms` | net8.0-windows | Windows Forms + WebView2 |
| `Bfocus.Widget.Wpf` | net8.0-windows | WPF + WebView2 |
| `Bfocus.Widget.Maui` | net10.0-android, -ios, -maccatalyst, -windows | .NET MAUI |

## Instalação

```bash
dotnet add package Bfocus.Widget.WinForms   # ou Bfocus.Widget.Wpf / Bfocus.Widget.Maui
```

No Windows, o WebView2 Runtime já vem no Windows 11 e nas atualizações do 10. Sem ele, o widget
abre no navegador padrão (modo navegador; veja abaixo).

## Antes de tudo: o `userHash` vem do seu servidor

No app vão **só** a chave pública `bf_pk_…` e o `userHash`. **Nunca** coloque `bf_whs_…` (segredo
do widget), `bf_live_…` ou `bf_sk_…` no app: qualquer um extrai do binário. O pacote recusa essas
chaves no `Init`.

O hash é `hex(HMAC-SHA256(segredo, "v1:" + user.externalId + ":" + customer.externalId))`,
calculado no **seu** backend. Os SDKs de servidor do bFocus já trazem `sign_widget_identity`:

```python
# servidor (Python)
from bfocus import sign_widget_identity
user_hash = sign_widget_identity(os.environ["BFOCUS_WIDGET_SECRET"], user_external_id, customer_external_id)
```

No app, devolva o hash por um endpoint autenticado seu e passe `UserHashProvider` (o pacote pede de
novo quando o servidor responde `WIDGET_USER_HASH_INVALID`). Ligue também "exigir sessão
verificada" no bFocus: a origem `app://…` é só um rótulo; a proteção real é o hash.

## Windows Forms

```csharp
using Bfocus.Widget;
using Bfocus.Widget.WinForms;

public partial class MainForm : Form
{
    private readonly BFocusWidget _widget;

    public MainForm()
    {
        InitializeComponent();
        _widget = BFocusWinFormsHost.CreateWidget(this);          // na thread de UI
        _widget.Error += (_, e) => Log.Warn($"bFocus: {e.Code} {e.Detail}");

        Controls.Add(new BFocusLauncherButton                      // botão redondo de 56 px com badge
        {
            Widget = _widget,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(ClientSize.Width - 80, ClientSize.Height - 80),
        });
        statusStrip.Controls.Add(new BFocusReleaseBadge { Widget = _widget }); // pílula "v4.2.0"

        Load += async (_, _) => await _widget.InitAsync(new BFocusConfig
        {
            PublishableKey = "bf_pk_…",
            AppId = "com.empresa.erp",                              // cadastrado em Integrações → Apps nativos
            User = new BFocusUser { ExternalId = usuario.Id, Name = usuario.Nome, Email = usuario.Email },
            Customer = new BFocusCustomer { ExternalId = cliente.Id, Name = cliente.Nome, Document = cliente.Cnpj },
            UserHashProvider = ct => meuBackend.GetBFocusHashAsync(ct),
            Notifications = true,                                   // aviso na bandeja quando o "•" acende
        });
    }
}
```

## WPF

```csharp
using Bfocus.Widget;
using Bfocus.Widget.Wpf;

public MainWindow()
{
    InitializeComponent();
    var widget = BFocusWpfHost.CreateWidget(this);
    launcher.Widget = widget;      // <bf:BFocusLauncherButton x:Name="launcher" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="16"/>
    versao.Widget = widget;        // <bf:BFocusReleaseBadge x:Name="versao"/>
    Loaded += async (_, _) => await widget.InitAsync(new BFocusConfig { /* igual ao WinForms */ });
}
```

(`xmlns:bf="clr-namespace:Bfocus.Widget.Wpf;assembly=Bfocus.Widget.Wpf"`)

## .NET MAUI

```csharp
using Bfocus.Widget;
using Bfocus.Widget.Maui;

var widget = BFocusMauiHost.CreateWidget();                        // na thread de UI
launcher.Widget = widget;                                          // BFocusLauncherButton (ContentView)
await widget.InitAsync(new BFocusConfig { PublishableKey = "bf_pk_…", AppId = AppInfo.PackageName, /* … */ });

// Push (o pacote não depende do Firebase: o seu app entrega o token e o data)
await widget.RegisterPushTokenAsync(fcmToken);                     // platform: "android" | "ios" (padrão pelo SO)
if (widget.HandlePush(remoteMessage.Data)) return;                 // true = era do bFocus; abre no chamado
```

No Android o canal é `WebViewCompat.addWebMessageListener` com a origem do embed como regra (nunca
`addJavascriptInterface`); o seletor de arquivos (`<input type=file multiple>`) e os downloads (pasta
Downloads) já estão ligados. No iOS/Mac a ponte confere `frameInfo.securityOrigin`. As telas abrem
como página modal de tela cheia; o banner de ciência não fecha pelo voltar nem por gesto. A consulta
ao launcher-state só roda com o app em primeiro plano.

## Configuração (`BFocusConfig`)

| Campo | Obrigatório | Padrão | Descrição |
|---|---|---|---|
| `PublishableKey` | sim | — | `bf_pk_…` |
| `AppId` | sim | — | id do app; a origem vira `app://<appId em minúsculas>` |
| `User` | sim | — | `ExternalId`, `Name?`, `Email?`, `Phone?` |
| `Customer` | sim | — | `ExternalId`, `Name?`, `Document?`, `Email?`, `Phone?`, `Website?` |
| `UserHash` | não | — | HMAC calculado no seu servidor |
| `UserHashProvider` | não | — | `Func<CancellationToken, Task<string?>>`; chamado no init e após `WIDGET_USER_HASH_INVALID` |
| `Product` | não | — | slug do produto |
| `Audience` | não | — | `external` \| `internal` \| `both` |
| `Locale` | não | idioma do sistema | `pt_BR` \| `en` \| `es` (pt* → pt_BR, es* → es, resto → en) |
| `ShowReleaseNotes` | não | `true` | `false` esconde o splash de novidades dentro dos chamados |
| `AutoShowReleaseBanner` | não | `true` | abre sozinho o banner de ciência |
| `ApiBaseUrl` / `EmbedBaseUrl` | não | produção | homologação e testes (`http://` só em 127.0.0.1/localhost) |
| `PollIntervalSeconds` | não | `60` | consulta ao launcher-state (mínimo 5) |
| `Notifications` | não | `false` | desktop: notificação do sistema quando o "•" acende |

## API

| Membro | Efeito |
|---|---|
| `InitAsync(config)` / `Init(config)` | valida, busca o hash, faz a 1ª consulta (nada em paralelo com ela) e liga a consulta periódica |
| `Open(target?)` | abre; `WidgetTarget.List` (padrão), `.New`, `.Chat(id?)`, `.Ticket(id)`. Já aberto/carregado: `bfocus:navigate` |
| `Close()` | esconde mantendo a WebView e consulta o launcher-state na hora |
| `OpenReleaseNotesHistory()` | histórico de versões |
| `RefreshAsync()` / `Refresh()` | consulta o launcher-state agora |
| `LogoutAsync()` / `Logout()` | para a consulta, cancela o push, apaga o `last_seen` e os dados da WebView da origem do embed |
| `RegisterPushTokenAsync(token, platform?)` | mobile: `POST /widget/push/devices` (registrado de novo a cada init) |
| `HandlePush(data)` | mobile: `true` se o push é do bFocus; abre no item certo |
| `SetAppForeground(bool)` | mobile: o host MAUI chama sozinho |
| eventos | `BadgeChanged(label)`, `ReleaseNotesChanged(state)`, `Error(code, detail)`, `Opened`, `Closed`, `PrimaryColorChanged(color)` |

Os eventos chegam na thread que criou o widget (o `SynchronizationContext` de UI). Os componentes
de UI são opcionais: dá para usar o próprio botão e só ouvir `BadgeChanged`.

### Erros (`Error`)

- `WIDGET_USER_HASH_INVALID` / `WIDGET_VERIFIED_SESSION_REQUIRED`: identidade recusada. Com
  `UserHashProvider`, o pacote pede um hash novo **uma vez** e repete; o evento só sai se a repetição
  também falhar. Sem provider, sai na hora. Cada código é avisado uma vez até a próxima resposta boa.
- Outros 4xx do launcher-state (ex.: `app://…` não cadastrado): o código do envelope ou `HTTP_<status>`.
- `WIDGET_CONFIG_FAILED` e demais `bfocus:error` do embed.
- Rede, tempo esgotado (15 s) e 5xx: silêncio; tenta de novo no próximo ciclo.

## Modo navegador

Sem WebView utilizável (sem o WebView2 Runtime no Windows; WebView antiga sem
`WebMessageListener` no Android), `Open()` abre o navegador padrão com `host=browser` e
`OpenReleaseNotesHistory()` abre o histórico. O badge e a pílula continuam pelo REST. O banner de
ciência não abre sozinho nesse modo (exige modal); o ponto da pílula continua avisando. Para forçar:
`new BFocusWinFormsHost(this) { WebViewAvailableOverride = false }`.

Sem host nenhum (`new BFocusWidget()`, ex.: app de console ou serviço de bandeja), só o `Core`
funciona e `Open()` também vai para o navegador.

## Estado local

`last_seen` (base do "•") e o token de push ficam em `%LOCALAPPDATA%\bfocus\widget-state.json`
(`FileStateStore`). Para guardar em outro lugar, implemente `IStateStore` e passe em
`new BFocusWidgetOptions { StateStore = … }`. Os dados do WebView2 ficam em
`%LOCALAPPDATA%\bfocus\WebView2\<appId>` (a pasta padrão, ao lado do .exe, não é gravável em
Program Files).

## Dependências

| Pacote | Dependência | Motivo |
|---|---|---|
| Core | `System.Text.Json` 8.0.6 (só netstandard2.0) | ler as respostas; já vem na caixa no net8.0 |
| WinForms / Wpf | `Microsoft.Web.WebView2` 1.0.3912.50 | a WebView |
| Maui | `Microsoft.Maui.Controls`, `Xamarin.AndroidX.WebKit` (Android) | a WebView e o `addWebMessageListener` |

## Desenvolvimento

```bash
dotnet test tests/Bfocus.Widget.Tests          # unitários (scenarios.json) + integração (node + mock-server)
dotnet build Bfocus.Widget.sln                 # tudo; MAUI precisa de `dotnet workload install maui`
```

- `tests/Bfocus.Widget.Tests/scenarios.json` é cópia gerada: rode
  `node widgets-native/conformance/generate.mjs` no monorepo, nunca edite à mão.
- A integração sobe `node widgets-native/conformance/mock-server.mjs 0`; fora do monorepo, defina
  `BFOCUS_MOCK_SERVER=<caminho do mock-server.mjs>` ou os testes de integração pulam.
- `examples/WinFormsExample` e `examples/WpfExample`: apps mínimos. Contra o servidor simulado:
  `BFOCUS_API=http://127.0.0.1:8787` e `BFOCUS_EMBED=http://127.0.0.1:8787/v1`.

## Licença

MIT. Copyright (c) 2026 Berni Software.
