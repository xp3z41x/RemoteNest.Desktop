# Builds the three release artifacts into dist\.
#
#   RemoteNest-Setup.exe          Inno Setup installer over the framework-dependent
#                                 build. Normal install into Program Files; requires
#                                 the .NET Desktop Runtime 10 (x64).
#   RemoteNest-Portable.exe       Self-contained single file. Runs anywhere, no
#                                 prerequisites; large because the runtime is embedded.
#   RemoteNest-Portable-Slim.exe  Framework-dependent single file. Small; requires the
#                                 .NET Desktop Runtime 10 (x64).
#
# Single-file compression stays off on purpose: high entropy is an antivirus heuristic.
param(
    [ValidateSet('all', 'installer', 'portable', 'slim')]
    [string]$Target = 'all',
    [switch]$SkipHashes
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'RemoteNest\RemoteNest.csproj'
$dist = Join-Path $root 'dist'

New-Item -ItemType Directory -Force $dist | Out-Null

function Invoke-Publish([string]$label, [string]$outDir, [string[]]$extra) {
    Write-Host "== $label" -ForegroundColor Cyan
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    dotnet publish $proj -c Release -r win-x64 -o $outDir @extra
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $label" }
}

function Copy-Artifact([string]$from, [string]$name) {
    $target = Join-Path $dist $name
    Copy-Item $from $target -Force
    $size = (Get-Item $target).Length / 1MB
    Write-Host ("   {0}: {1:N1} MB" -f $name, $size) -ForegroundColor Green
}

# The Win32 version resource must carry the identity from Directory.Build.props;
# an empty CompanyName/FileVersion is itself an antivirus heuristic.
function Assert-Metadata([string]$exe) {
    $info = (Get-Item $exe).VersionInfo
    foreach ($field in 'CompanyName', 'ProductName', 'LegalCopyright', 'FileVersion', 'ProductVersion') {
        if ([string]::IsNullOrWhiteSpace($info.$field)) { throw "$exe is missing $field in its version resource" }
    }
    Write-Host ("   metadata: {0} {1}" -f $info.ProductName, $info.FileVersion) -ForegroundColor DarkGray
}

function Find-Iscc() {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"   # winget installs per-user
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup"
}

if ($Target -in @('all', 'portable')) {
    $out = Join-Path $root 'publish-portable'
    Invoke-Publish 'portable (self-contained, single file)' $out @('-p:PublishSingleFile=true', '--self-contained', 'true')
    $exe = Join-Path $out 'RemoteNest.exe'
    Assert-Metadata $exe
    Copy-Artifact $exe 'RemoteNest-Portable.exe'
}

if ($Target -in @('all', 'slim')) {
    $out = Join-Path $root 'publish-slim'
    Invoke-Publish 'slim (framework-dependent, single file)' $out @('-p:PublishSingleFile=true', '--self-contained', 'false')
    $exe = Join-Path $out 'RemoteNest.exe'
    Assert-Metadata $exe
    Copy-Artifact $exe 'RemoteNest-Portable-Slim.exe'
}

if ($Target -in @('all', 'installer')) {
    # No PublishSingleFile here: the installer lays the app out as normal files.
    $out = Join-Path $root 'publish-installer'
    Invoke-Publish 'installer payload (framework-dependent)' $out @('--self-contained', 'false')
    Assert-Metadata (Join-Path $out 'RemoteNest.exe')

    $iscc = Find-Iscc
    Write-Host "== building installer with $iscc" -ForegroundColor Cyan
    & $iscc (Join-Path $root 'installer.iss') /Q
    if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
    Copy-Artifact (Join-Path $root 'output\RemoteNest-Setup.exe') 'RemoteNest-Setup.exe'
}

if (-not $SkipHashes) {
    $lines = Get-ChildItem $dist -Filter *.exe | Sort-Object Name | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
    }
    $sums = Join-Path $dist 'SHA256SUMS.txt'
    Set-Content -Path $sums -Value $lines -Encoding ascii
    Write-Host "`nSHA256SUMS.txt" -ForegroundColor Cyan
    $lines | ForEach-Object { Write-Host "   $_" }
}
