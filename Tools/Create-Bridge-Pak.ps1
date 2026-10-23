param(
    [string]$DivineExe = "",
    [switch]$InstallForTesting
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$settings = Join-Path $project 'Data\tool-settings.json'
if (-not $DivineExe -and (Test-Path -LiteralPath $settings)) {
    try {
        $stored = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
        $DivineExe = [string]$stored.DivinePath
    } catch {
        Write-Warning 'Gespeicherter Divine-Pfad konnte nicht gelesen werden.'
    }
}
if (-not $DivineExe) { $DivineExe = Read-Host 'Vollstaendigen Pfad zu Divine.exe eingeben' }
$DivineExe = $DivineExe.Trim().Trim('"')
if (-not (Test-Path -LiteralPath $DivineExe -PathType Leaf) -or
    -not [System.IO.Path]::GetFileName($DivineExe).Equals('Divine.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Es muss eine vorhandene Divine.exe aus LSLib ausgewaehlt werden.'
}
$lua = Join-Path $project 'BridgeMod\Mods\BG3ItemManagerBridge\ScriptExtender\Lua\BootstrapServer.lua'
if (-not (Test-Path -LiteralPath $lua -PathType Leaf)) { throw "Bridge-Quelldatei fehlt: $lua" }
$output = Join-Path $project 'BridgeBuild'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$pak = Join-Path $output 'BG3ItemManagerBridge.pak'
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('BG3ItemManagerBridge_' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Copy-Item -LiteralPath (Join-Path $project 'BridgeMod\Mods') -Destination (Join-Path $staging 'Mods') -Recurse -Force
    if (Test-Path -LiteralPath $pak) { Remove-Item -LiteralPath $pak -Force }
    & $DivineExe -g bg3 -a create-package -s $staging -d $pak -c lz4hc
    if ($LASTEXITCODE -ne 0) { throw "Divine.exe ist mit Fehlercode $LASTEXITCODE fehlgeschlagen." }
    if (-not (Test-Path -LiteralPath $pak -PathType Leaf) -or (Get-Item -LiteralPath $pak).Length -lt 128) {
        throw 'Das erstellte PAK fehlt oder ist ungewoehnlich klein.'
    }
    Write-Host "PAK erfolgreich erstellt: $pak" -ForegroundColor Green
    if ($InstallForTesting) {
        Write-Warning 'TEST NUR IM EINZELSPIELER / IN GETRENNTEM MOD-PROFIL! Multiplayer-Kompatibilitaet ungetestet.'
        $answer = Read-Host 'PAK jetzt in den lokalen BG3-Modordner kopieren? (JA/NEIN)'
        if ($answer -eq 'JA') {
            $dest = Join-Path $env:LOCALAPPDATA "Larian Studios\Baldur's Gate 3\Mods"
            New-Item -ItemType Directory -Force -Path $dest | Out-Null
            Copy-Item -LiteralPath $pak -Destination (Join-Path $dest 'BG3ItemManagerBridge.pak') -Force
            Write-Host "Kopiert nach: $dest"
            Write-Host 'Die Mod muss danach im Mod-Manager aktiviert und die Ladereihenfolge gespeichert werden.'
        }
    }
    Write-Host 'Nichts am Spielstand geaendert. Der Data-Cache des Item Managers bleibt unberuehrt.'
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
