param(
    [Parameter(Mandatory = $true)]
    [string]$UpdateZip
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$zipPath = (Resolve-Path -LiteralPath $UpdateZip).Path
$exe = Join-Path $project 'BG3ItemManager.exe'
if (Get-Process -Name BG3ItemManager -ErrorAction SilentlyContinue) {
    throw 'Bitte den BG3 Item Manager vor dem Update schliessen.'
}
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('BG3ItemManagerUpdate_' + [guid]::NewGuid().ToString('N'))
$backup = Join-Path $project 'BG3ItemManager.exe.bak'
try {
    Expand-Archive -LiteralPath $zipPath -DestinationPath $staging -Force
    $newExe = Get-ChildItem -LiteralPath $staging -Recurse -Filter 'BG3ItemManager.exe' -File |
        Select-Object -First 1
    if (-not $newExe) { throw 'Im Update-ZIP wurde keine BG3ItemManager.exe gefunden.' }
    if (Test-Path -LiteralPath $exe) { Copy-Item -LiteralPath $exe -Destination $backup -Force }
    Copy-Item -LiteralPath $newExe.FullName -Destination $exe -Force
    Write-Host 'EXE aktualisiert.' -ForegroundColor Green
    Write-Host 'Der bestehende Data-Ordner wurde NICHT geloescht, verschoben oder ueberschrieben.'
    Write-Host "Backup der bisherigen EXE: $backup"
} catch {
    if (Test-Path -LiteralPath $backup) {
        Copy-Item -LiteralPath $backup -Destination $exe -Force
    }
    throw
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
