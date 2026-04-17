# RemoteNest — Release Notes

Datas em YYYY-MM-DD. Releases publicados em
https://github.com/xp3z41x/RemoteNest.Desktop/releases.

## 1.1.0 — 2026-04-16 — Stability, security & architecture hardening

Release de consolidação: 42 fixes em três frentes (segurança, dados,
UI/UX, arquitetura) + suite de testes xUnit. Zero mudanças de
comportamento user-visible quando tudo está em operação normal;
ganhos aparecem em casos de borda (unicode, concorrência, encoding
de `.rdp`, falhas de rede) e em robustez de longo prazo.

**Segurança**

- `RdpLauncherService` reescrito: credenciais agora passadas ao
  `cmdkey.exe` via `ProcessStartInfo.ArgumentList` (escaping correto
  de aspas/espaços via `CommandLineToArgvW`) em vez de interpolação
  de string, eliminando injeção via host/usuário hostil.
- Arquivo `.rdp` temporário por sessão gravado em
  `%LOCALAPPDATA%\RemoteNest\sessions\` com nome GUID, sem persistência
  entre sessões, removido no `ProcessExit` mesmo em crash.
- Validação de host rejeita cedo valores malformados com mensagem
  localizada (`InvalidHost`).
- Limpeza de credential cache (`cmdkey /delete`) garantida mesmo
  quando `mstsc.exe` termina anormalmente.

**Dados**

- Import de `.rdp` com BOM UTF-16 LE (encoding default do mstsc) agora
  detectado via `StreamReader(..., detectEncodingFromByteOrderMarks: true)`
  em vez de ler como ASCII e ver símbolos corrompidos.
- Parser de `full address` suporta IPv6 entre colchetes
  (`[fe80::1]:3390`) além de IPv4/DNS com/sem porta.
- `domain:s:` como linha solta no `.rdp` agora é importado (bug
  silencioso anterior: só parseava `DOMAIN\user` via username).
- `RecordConnectionAsync` agora faz `ExecuteUpdateAsync` atômico
  (`COUNT = COUNT + 1`) em vez de read-modify-write — duas conexões
  simultâneas não perdem mais contagens.
- Busca case-insensitive via `EF.Functions.Like` com overload de 3
  argumentos e `ESCAPE '\'` explícito — caracteres `%` e `_` no nome
  do perfil não quebram mais o filtro (antes produziam zero
  resultados silenciosamente).
- Import JSON rejeita entries sem `Name` ou `Host` e força
  `AutoConnectOnStartup=false` para não sequestrar startup via
  payload hand-crafted.

**UI / UX**

- Todos os `[RelayCommand]` async agora com
  `AllowConcurrentExecutions=false` — duplo-clique em Connect,
  Save, Import etc. não dispara duas operações.
- Cores de erro de validação migradas de `IndianRed` hardcoded para
  `SystemControlErrorTextForegroundBrush` — respeita tema atual
  (Light/DarkBlue/Dark/System).
- Diálogos (`ConnectionEditor`, `SettingsDialog`) agora com
  `SizeToContent=Height`, `MinHeight`/`MaxHeight`, redimensionáveis,
  `IsDefault`/`IsCancel` nos botões e `KeyBinding Esc` → Cancel.
- `TreeView` com binding de `IsExpanded` escopado a
  `ItemContainerStyle` do template de grupo apenas — profiles não
  herdam mais o binding e não disparam mais warnings de MVVM.
- `AutomationProperties.Name` em botões icon-only (Settings, New).

**Arquitetura**

- `IDialogService` / `DialogService` encapsulam `SaveFileDialog` /
  `OpenFileDialog` / `MessageBox` — ViewModels testáveis sem janelas.
- `FileLoggerProvider` grava logs diários em
  `%LOCALAPPDATA%\RemoteNest\logs\` com retenção de 7 dias. Exceções
  em logging são engolidas silenciosamente (nunca derrubam o app).
- Três handlers globais de exceção instalados em `App.xaml.cs`:
  `DispatcherUnhandledException` (marca `Handled=true` e loga),
  `AppDomain.UnhandledException` (log-only), `TaskScheduler.Unobserved`
  (`SetObserved()` + log). App não morre mais por exceções em
  background task.
- `AppDbContextFactory` aceita `DbContextOptions<AppDbContext>`
  injetadas, permitindo SQLite in-memory em testes sem mexer no
  caminho de produção.
- `ILogger<T>` injetado em todos os ViewModels e Services; `App.xaml.cs`
  compõe `LoggerFactory` com provider de arquivo.

**Localização**

- `MainViewModel` / `ConnectionListViewModel` subscrevem
  `TranslationSource.PropertyChanged` → status text, nomes de grupo
  e filtros re-renderizam na hora ao trocar idioma.
- `StringComparer.CurrentCultureIgnoreCase` em ordenações (antes
  `Ordinal`, que colocava `Ágora` depois de `Zebra` em pt-BR).

**Feature**

- Estado `IsExpanded` de cada grupo preservado ao recarregar a lista
  (busca, refresh, import).

**Testes**

- Novo projeto `RemoteNest.Tests` (xUnit 2.9 + FluentAssertions 6.12
  + Microsoft.Data.Sqlite in-memory). 33 testes cobrindo:
  - `EncryptionService`: roundtrip, empty, unicode
    (`senha-日本語-🔐-ção`), base64 inválido, ciphertext adulterado.
  - `ParseFullAddress`: IPv4/IPv6/DNS com e sem porta, porta fora de
    range, fallback malformado.
  - `EscapeLike`: `%`, `_`, `\` escapados corretamente.
  - `ConnectionService` integração (SQLite real, não InMemory
    provider): CRUD, Duplicate, RecordConnection incremento atômico
    x10, busca case-insensitive, escape de wildcard LIKE, import
    `.rdp` UTF-16 LE BOM + UTF-8, import JSON com validação e
    `AutoConnect=false` forçado, `GetGroups` distinto.
- `InternalsVisibleTo` adicionado ao csproj do app para dar acesso a
  métodos `internal` (`ParseFullAddress`, `EscapeLike`).

**Bugs descobertos pelos testes (e corrigidos)**

- `.rdp` import descartava `domain:s:CORP` silenciosamente.
- `SearchAsync` retornava zero resultados para queries com `%` ou `_`
  por falta da cláusula `ESCAPE '\'` no SQL gerado.

Ambos os bugs existiam desde v1.0.0 e passaram despercebidos por ~5
releases. Suite de testes agora previne regressão.

**Verificação**

SHA256 de cada asset publicado em `SHA256SUMS.txt` anexo ao release.

---

## 1.0.7 — 2026-04-14 — AV hardening + modern project structure

**Confiança & packaging**

- Portable single-file agora **sem compressão interna**
  (`EnableCompressionInSingleFile=false`) — reduz drasticamente a
  entropia do binário, endereçando heurísticas de antivírus que
  confundiam bundles .NET comprimidos com packers de malware. Tamanho
  do portable sobe de ~73 MB para ~150 MB; funcionalidade idêntica.
- Metadata Win32 completa embutida: Company, Product, Copyright,
  FileVersion, InformationalVersion, Description, AssemblyTitle —
  visível em Propriedades → Detalhes do Windows Explorer.
- Novo `app.manifest`: `asInvoker` execution level + `PerMonitorV2`
  DPI + supportedOS Win7/8/8.1/10/11 + `activeCodePage=UTF-8` +
  `longPathAware=true`.
- Símbolos de debug embutidos (`DebugType=embedded`) em vez de
  stripados — AVs tratam PDB embutido como sinal de build legítimo.
- `IncludeNativeLibrariesForSelfExtract=true` — DLLs nativas extraídas
  para `%TEMP%\.net\` em vez de carregadas via mmap in-memory
  (padrão mais reconhecido por AVs).
- Installer (Inno Setup): compressão de `lzma2/ultra64` → `lzma2/max`
  (entropia menor); adicionadas VersionInfo* + AppPublisherURL +
  AppSupportURL + AppUpdatesURL + AppContact + AppCopyright.

**Estrutura de projeto (skill dotnet-project-structure)**

- `Directory.Build.props` centraliza metadata e defaults de publish.
- `global.json` pinning SDK 8.0.x (rollForward=latestFeature).
- `RELEASE_NOTES.md` (este arquivo).
- `.github/workflows/ci.yml` (build + assert de metadata em cada push).
- `.github/workflows/release.yml` (tag v*.*.* → build + Inno +
  SHA256SUMS + release).
- `docs/AV-FAQ.md` (guia para usuários que vejam alerta de AV).

**Verificação**

SHA256 de cada asset publicado em `SHA256SUMS.txt` anexo ao release.

```
<sha256>  RemoteNest-Setup.exe
<sha256>  RemoteNest-Portable.exe
```

---

## 1.0.6 — 2026-04-14 — Installer hardening + portable release

- Installer: `ArchitecturesAllowed` `x64compatible` → `x64`.
- Installer: `MinVersion` → `10.0.17763` (.NET 8 real floor).
- Installer: `InitializeSetup` envolto em `try/except` (fix para falha
  silenciosa em Windows 10 IoT LTSC).
- Installer: `CloseApplications=force` / `RestartApplications=no`.
- Primeiro portable single-file anexo ao release.

## 1.0.5 — Theming

- Botões uniformes em toolbar + detail panel.
- Seletor de tema: Light / Dark Blue / Dark / System.

## 1.0.4 — Install mode

- Force install-mode dialog em cada execução do setup.

## 1.0.3 — Automation

- Auto-start com Windows + auto-connect por perfil.

## 1.0.2 — Installer modes

- Installer per-user ou per-machine via MsgBox na abertura.

## 1.0.1 — Bug fix

- Fix duplicate launch em double-click.

## 1.0.0 — Initial release

- Gerenciamento de perfis RDP, grupos, TreeView, busca em tempo real,
  senhas cifradas via DPAPI, launch via `mstsc.exe`, import/export
  JSON, import de `.rdp` files, dashboard, tema ModernWpfUI.
