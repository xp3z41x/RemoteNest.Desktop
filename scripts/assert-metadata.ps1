param([string]$ExePath = "publish-portable\RemoteNest.exe")

$info = (Get-Item $ExePath).VersionInfo
$info | Format-List CompanyName, ProductName, ProductVersion, FileVersion, LegalCopyright, FileDescription, InternalName, OriginalFilename

$missing = @()
if ([string]::IsNullOrEmpty($info.CompanyName))    { $missing += "CompanyName" }
if ([string]::IsNullOrEmpty($info.ProductName))    { $missing += "ProductName" }
if ([string]::IsNullOrEmpty($info.LegalCopyright)) { $missing += "LegalCopyright" }
if ([string]::IsNullOrEmpty($info.FileVersion))    { $missing += "FileVersion" }
if ([string]::IsNullOrEmpty($info.ProductVersion)) { $missing += "ProductVersion" }

if ($missing.Count -gt 0) {
    Write-Host "MISSING: $($missing -join ', ')" -ForegroundColor Red
    exit 1
}

$size = (Get-Item $ExePath).Length
Write-Host ("OK: all metadata fields present. Size: {0:N0} bytes ({1:N1} MB)" -f $size, ($size/1MB)) -ForegroundColor Green
