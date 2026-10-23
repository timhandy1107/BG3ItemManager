$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'Zum Bauen ist das .NET 8 SDK erforderlich. Auf dem Ziel-PC zum Starten der EXE nicht.' -ForegroundColor Yellow
    exit 1
}
dotnet publish .\BG3ItemManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Fertig: $PSScriptRoot\publish\BG3ItemManager.exe" -ForegroundColor Green
