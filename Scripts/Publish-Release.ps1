param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$MainProject = Join-Path $ProjectRoot "UnifiedGameLauncher.csproj"
$UpdaterProject = Join-Path $ProjectRoot "Updater\LUDARYX.Updater.csproj"
$MainPublish = Join-Path $ProjectRoot "bin\$Configuration\net8.0-windows\$Runtime\publish"
$UpdaterPublish = Join-Path $ProjectRoot "Updater\bin\$Configuration\net8.0-windows\$Runtime\publish"

Write-Host "Publicando LUDARYX..."
dotnet publish $MainProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    throw "A publicação do LUDARYX falhou. Corrija os erros acima antes de continuar."
}

if (-not (Test-Path -LiteralPath $MainPublish)) {
    throw "A pasta de publicação principal não foi criada: $MainPublish"
}

Write-Host "Publicando LUDARYX Updater..."
dotnet publish $UpdaterProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    throw "A publicação do LUDARYX Updater falhou. Corrija os erros acima antes de continuar."
}

$UpdaterExe = Join-Path $UpdaterPublish "LUDARYX.Updater.exe"
if (-not (Test-Path -LiteralPath $UpdaterExe)) {
    throw "LUDARYX.Updater.exe não foi encontrado após a publicação."
}

Copy-Item -LiteralPath $UpdaterExe -Destination (Join-Path $MainPublish "LUDARYX.Updater.exe") -Force

Write-Host ""
Write-Host "Publicação concluída:" -ForegroundColor Green
Write-Host $MainPublish
Write-Host ""
Write-Host "Arquivos principais esperados:"
Write-Host "  LUDARYX.exe"
Write-Host "  LUDARYX.Updater.exe"
Write-Host ""
Write-Host "Agora compile LUDARYX-Installer-1.0.2.iss no Inno Setup."
