# Changelog

Formato: [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). Versões: [SemVer](https://semver.org/lang/pt-BR/).
Os quatro pacotes (`Bfocus.Widget.Core`, `.WinForms`, `.Wpf`, `.Maui`) saem sempre com a mesma versão.

## [0.1.0] - 2026-09-15

### Adicionado
- `Bfocus.Widget.Core` (netstandard2.0 + net8.0): `BFocusWidget` com `Init`/`Open`/`Close`/
  `OpenReleaseNotesHistory`/`Refresh`/`Logout`, `RegisterPushTokenAsync`/`HandlePush`, eventos
  `BadgeChanged`, `ReleaseNotesChanged`, `Error`, `Opened`, `Closed` e `PrimaryColorChanged`.
- Host Protocol v1: URL do embed com os parâmetros no fragmento, payload do usuário byte a byte
  igual ao widget web, ponte com checagem de origem, `bfocus:navigate` para deep link.
- `launcher-state` com `HttpClient` injetável, 15 s por chamada, fila sem chamadas paralelas à
  primeira, consulta a cada `PollIntervalSeconds` só com o widget fechado, renovação do
  `userHash` pelo `UserHashProvider`.
- Badge (`•`, contagem, `99+`) com `last_seen` persistente (`FileStateStore` em
  `%LOCALAPPDATA%\bfocus`), pílula de versão e banner de ciência automático.
- Modo navegador quando não há WebView utilizável.
- `Bfocus.Widget.WinForms` e `Bfocus.Widget.Wpf` (WebView2): painel 400 × 620, banner modal sem
  fechar, `BFocusLauncherButton`, `BFocusReleaseBadge`, "salvar como" nos downloads, navegação
  travada na origem do embed, notificação na bandeja, tela "sem conexão".
- `Bfocus.Widget.Maui` (net10.0 Android/iOS/Mac Catalyst/Windows): páginas modais em tela cheia,
  ponte nativa por plataforma (`addWebMessageListener`, `WKScriptMessageHandler`, WebView2),
  seletor de arquivos no Android, downloads e push.
