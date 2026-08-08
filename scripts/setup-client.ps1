# Book of Travels — Braid private-server client setup (Windows)
# Downloads and installs the BepInEx runtime (official BepInExPack) + the
# BotMaster plugin into your own Steam install. No game files are modified.
param(
    [string]$MasterHost = "braid.flightlessbirdlabs.io",
    [int]$MasterPort = 1234,
    [string]$GameDir = ""
)
$ErrorActionPreference = "Stop"

$repo = "JRustyHaner/book-of-travels-server"
$base = "https://github.com/$repo/releases/latest/download"

if (-not $GameDir) {
    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Book of Travels",
        "D:\SteamLibrary\steamapps\common\Book of Travels",
        "E:\SteamLibrary\steamapps\common\Book of Travels"
    )
    $GameDir = $candidates | Where-Object { Test-Path "$_\BookOfTravels.exe" } | Select-Object -First 1
}
if (-not $GameDir -or -not (Test-Path "$GameDir\BookOfTravels.exe")) {
    Write-Host "ERROR: game not found. Pass -GameDir <path> or run it from a Steam library." -ForegroundColor Red
    exit 1
}
Write-Host "==> Braid client setup"
Write-Host "    server: $MasterHost`:$MasterPort"
Write-Host "    game:   $GameDir"

# 1) BepInEx runtime — official BepInExPack (Windows build) via Thunderstore
$bep = "$env:TEMP\BepInExPack.zip"
if (-not (Test-Path "$GameDir\winhttp.dll")) {
    Write-Host "==> downloading BepInExPack (official)"
    Invoke-WebRequest -Uri "https://thunderstore.io/api/experimental/package/BepInEx/BepInExPack/" -OutFile "$env:TEMP\bepinexpack.json"
    $dl = (Get-Content "$env:TEMP\bepinexpack.json" | ConvertFrom-Json).latest.download_url
    Invoke-WebRequest -Uri $dl -OutFile $bep
    Expand-Archive -Path $bep -DestinationPath $GameDir -Force
    Remove-Item $bep
    Write-Host "    BepInEx runtime installed"
}

# 2) plugin (refresh to latest)
Write-Host "==> downloading BotMaster plugin"
Invoke-WebRequest -Uri "$base/botmaster-plugin.zip" -OutFile "$env:TEMP\bot-plugin.zip"
$plugDir = "$GameDir\BepInEx\plugins"
New-Item -ItemType Directory -Force -Path $plugDir | Out-Null
Expand-Archive -Path "$env:TEMP\bot-plugin.zip" -DestinationPath $plugDir -Force
Remove-Item "$env:TEMP\bot-plugin.zip"

# 3) config
$cfgDir = "$GameDir\BepInEx\config"
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
@"
[general]
role = "client"
masterHost = "$MasterHost"
masterPort = $MasterPort
"@ | Set-Content -Path "$cfgDir\dev.botmaster.plugin.cfg" -Encoding ASCII
Write-Host "    config written (role=client)"

Write-Host ""
Write-Host "==> done. Launch Book of Travels from Steam and log in with any"
Write-Host "    email + password (account is created automatically)."
Write-Host "    First launch may be slower (BepInEx preloads once)."
