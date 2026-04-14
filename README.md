# RemoteNest — Windows RDP Connection Manager

A desktop application for managing RDP connection profiles on Windows. It stores connection configurations and launches the native `mstsc.exe` (Remote Desktop) client with the correct parameters.

Inspired by [Remmina](https://remmina.org/) on Linux.

## Features

- **Connection profiles** — Save host, port, credentials, display and resource settings
- **Groups** — Organize connections in collapsible groups (TreeView)
- **Real-time search** — Filter connections by name or host
- **Encrypted passwords** — DPAPI (CurrentUser scope) via `System.Security.Cryptography.ProtectedData`
- **RDP launch** — Generates temp `.rdp` file, injects credentials via `cmdkey.exe`, launches `mstsc.exe`
- **Credential cleanup** — Removes `cmdkey` entries and temp files after 5 seconds
- **Import/Export** — Export profiles to JSON (without passwords), import from JSON
- **Import .rdp files** — Parse existing `.rdp` files into connection profiles
- **Duplicate profiles** — One-click duplication with "(cópia)" suffix
- **Dashboard** — Connection count and recently used profiles at a glance
- **Modern UI** — ModernWpfUI theme for a flat, Windows 11-compatible look

## Tech Stack

| Component         | Technology                              |
|-------------------|-----------------------------------------|
| Framework         | .NET 8, WPF                             |
| Architecture      | MVVM (CommunityToolkit.Mvvm)            |
| UI Theme          | ModernWpfUI                             |
| Database          | SQLite via EF Core                      |
| Password Security | DPAPI (Windows Data Protection API)     |

## Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

## Build & Run

```bash
# Clone and build
cd RemoteNest.Desktop
dotnet build RemoteNest/RemoteNest.csproj

# Run
dotnet run --project RemoteNest/RemoteNest.csproj
```

## Publish as Single-File Executable

```bash
dotnet publish RemoteNest/RemoteNest.csproj -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=false \
  -p:IncludeAllContentForSelfExtract=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish-portable
```

The output `RemoteNest.exe` will be in `./publish-portable/`. Compression is
deliberately **disabled** to keep the binary's entropy in the normal range —
this reduces false positives from antivirus heuristics that flag compressed
.NET single-file bundles as packed malware. Expect ~150 MB.

## Verifying Downloads

Every GitHub release includes a `SHA256SUMS.txt` file alongside the binaries.
Verify the integrity of your download by comparing hashes:

```powershell
Get-FileHash .\RemoteNest-Portable.exe -Algorithm SHA256
```

The output should match the line for the corresponding file in
`SHA256SUMS.txt` from the [release page](https://github.com/xp3z41x/RemoteNest.Desktop/releases).

## Antivirus Warnings

Unsigned .NET single-file bundles are occasionally flagged as malware by
antivirus heuristics. This is a known, industry-wide problem. See
[docs/AV-FAQ.md](docs/AV-FAQ.md) for why it happens, how to verify the
binary is authentic, and how to submit a false-positive report to your
AV vendor.

## Data Storage

- **Database:** `%APPDATA%\RemoteNest\remotenest.db` (SQLite)
- **Temp RDP files:** `%TEMP%\RemoteNest\` (auto-deleted after launch)

## Project Structure

```
RemoteNest/
├── Models/
│   └── ConnectionProfile.cs        # Data model
├── Data/
│   ├── AppDbContext.cs              # EF Core context
│   └── Migrations/                  # EF Core migrations
├── Services/
│   ├── IConnectionService.cs        # CRUD interface
│   ├── ConnectionService.cs         # CRUD + import/export
│   ├── IEncryptionService.cs        # DPAPI interface
│   ├── EncryptionService.cs         # DPAPI wrapper
│   ├── IRdpLauncherService.cs       # RDP launch interface
│   └── RdpLauncherService.cs        # .rdp generation + mstsc.exe launch
├── ViewModels/
│   ├── MainViewModel.cs             # Root VM (toolbar, detail panel)
│   ├── ConnectionListViewModel.cs   # Left panel TreeView
│   └── ConnectionEditorViewModel.cs # Editor dialog
├── Views/
│   ├── MainWindow.xaml              # Main layout
│   ├── ConnectionEditorView.xaml    # Editor dialog (tabbed form)
│   └── *.xaml.cs                    # Code-behind
├── Converters/                      # WPF value converters
├── App.xaml                         # Theme resources
└── App.xaml.cs                      # Startup + DI wiring
```

## License

Private project.
