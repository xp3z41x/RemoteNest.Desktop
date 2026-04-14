# RemoteNest — Release Notes

Datas em YYYY-MM-DD. Releases publicados em
https://github.com/xp3z41x/RemoteNest.Desktop/releases.

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
