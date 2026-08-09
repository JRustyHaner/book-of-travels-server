# install-client-windows.ps1 --- installs the Braid client mod into a Windows
# Book of Travels install (Steam). Downloads the latest client bundle from the
# Braid GitHub release; adds BepInEx + the plugin, modifies nothing else.
#
# Usage: powershell -ExecutionPolicy Bypass -File install-client-windows.ps1
# Params: -MasterHost, -MasterPort, -GameDir
param(
    [string]$MasterHost = "braid-connect.flightlessbirdlabs.io",
    [int]$MasterPort = 1234,
    [string]$GameDir = "${env:ProgramFiles(x86)}\Steam\steamapps\common\Book of Travels",
    [string]$Version = "latest"
)

$ErrorActionPreference = "Stop"
$base = "https://github.com/JRustyHaner/book-of-travels-server/releases/$Version/download"

Write-Host "==> Braid client installer"
Write-Host "    master: $MasterHost`:$MasterPort"
Write-Host "    game:   $GameDir"

if (-not (Test-Path "$GameDir\BookOfTravels.exe")) {
    Write-Error "Book of Travels not found at '$GameDir'. Pass -GameDir <path>."
    exit 1
}

# Remove any previous mod install (old BepInEx 5 / mismatched versions) so the
# bundle extracts cleanly. These are additive mod files; the game itself is untouched.
foreach ($old in @("$GameDir\BepInEx", "$GameDir\winhttp.dll", "$GameDir\doorstop_config.ini", "$GameDir\.doorstop_version")) {
    if (Test-Path $old) { Remove-Item -Recurse -Force $old }
}

$tmp = Join-Path $env:TEMP "braid-client"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

Write-Host "==> downloading bundle"
Invoke-WebRequest "$base/braid-client-windows.zip" -OutFile "$tmp\bundle.zip" -UseBasicParsing

Write-Host "==> extracting into game folder (additive, no game files touched)"
Expand-Archive "$tmp\bundle.zip" -DestinationPath $GameDir -Force

$cfg = "$GameDir\BepInEx\config\dev.botmaster.plugin.cfg"
New-Item -ItemType Directory -Force -Path (Split-Path $cfg) | Out-Null
@"
[general]
role = "client"
masterHost = "$MasterHost"
masterPort = $MasterPort
"@ | Set-Content -Path $cfg -Encoding UTF8

Write-Host "==> done. Launch Book of Travels normally via Steam."
Write-Host "    (Log in with any email/password --- the Braid server provisions the account.)"
