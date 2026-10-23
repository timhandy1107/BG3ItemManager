param([string]$GamePath = "")
$ErrorActionPreference = "Stop"
$project = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$src = Join-Path $project "BridgeLocal\LocalBridge.lua"
if (-not (Test-Path -LiteralPath $src -PathType Leaf)) {
    throw "Fehlt: $src. Kopiere erst den Ordner BridgeLocal aus GitHub neben die EXE."
}

if (-not $GamePath) {
    $settingsFile = Join-Path $project "Data\game-settings.json"
    if (Test-Path -LiteralPath $settingsFile) {
        try { $GamePath = [string]((Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json).InstallationPath) }
        catch { Write-Warning "BG3-Installationspfad konnte nicht eingelesen werden." }
    }
}
if (-not $GamePath) { $GamePath = Read-Host "Vollstaendigen BG3-Installationsordner angeben" }
$GamePath = $GamePath.Trim().Trim('"')
if (-not (Test-Path -LiteralPath (Join-Path $GamePath "Data\Shared.pak") -PathType Leaf)) {
    throw "Das ist kein gueltiger BG3-Installationsordner (Data\Shared.pak fehlt)."
}
$bin = Join-Path $GamePath "bin"
if (-not (Test-Path -LiteralPath $bin -PathType Container)) { throw "BG3 bin-Ordner fehlt." }
$configFile = Join-Path $bin "ScriptExtenderSettings.json"

# Do not overwrite unrelated Script Extender preferences.
if (Test-Path -LiteralPath $configFile) {
    Copy-Item -LiteralPath $configFile -Destination ($configFile + ".bg3itemmanager.bak") -Force
    $settings = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
} else {
    $settings = [pscustomobject]@{}
}
if ($null -eq $settings.PSObject.Properties["CreateConsole"]) {
    $settings | Add-Member -NotePropertyName "CreateConsole" -NotePropertyValue $true
} else {
    $settings.CreateConsole = $true
}
$settings | ConvertTo-Json -Depth 24 | Set-Content -LiteralPath $configFile -Encoding UTF8

# The extender sandbox directory is not BG3's Mods directory.
$extenderDir = Join-Path $env:LOCALAPPDATA "Larian Studios\Baldur's Gate 3\Script Extender\BG3ItemManager"
New-Item -ItemType Directory -Force -Path $extenderDir | Out-Null
$dest = Join-Path $extenderDir "LocalBridge.lua"
Copy-Item -LiteralPath $src -Destination $dest -Force

Write-Host ""
Write-Host "Lokale Bruecke vorbereitet. Keine PAK, keine Mod-Ladereihenfolge geaendert." -ForegroundColor Green
Write-Host "Datei: $dest"
Write-Host "Einstellungen: $configFile"
Write-Host ""
Write-Host "1. BG3 neu starten, TEST-Spielstand laden."
Write-Host "2. SE-Konsole fokussieren, Enter druecken, 'server' eingeben und Enter."
Write-Host "3. Folgenden Befehl in die Server-Konsole kopieren und Enter druecken:"
Write-Host ""
$command = 'assert(Ext.Utils.LoadString(assert(Ext.IO.LoadFile("BG3ItemManager/LocalBridge.lua"))))()'
Write-Host $command -ForegroundColor Yellow
Write-Host ""
Write-Host "4. Dann in deiner vorhandenen Item-Manager-EXE 'Bruecke pruefen' klicken."
Write-Host "   Nach jedem Neustart von BG3 muss der Konsolenbefehl erneut ausgefuehrt werden."
Write-Host "   Der SE-API-Test in einer echten BG3-Sitzung steht noch aus."
