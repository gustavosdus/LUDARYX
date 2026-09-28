param(
    [string]$ExpectedVersion = "1.1.0"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

function Require-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Arquivo obrigatório não encontrado: $Path"
    }
}

function Require-Equal([string]$Label, [string]$Actual, [string]$Expected) {
    if ($Actual -ne $Expected) {
        throw "$Label divergente. Encontrado: '$Actual'. Esperado: '$Expected'."
    }
    Write-Host "[OK] $Label = $Actual" -ForegroundColor Green
}

$mainProject = Join-Path $ProjectRoot "UnifiedGameLauncher.csproj"
$updaterProject = Join-Path $ProjectRoot "Updater\LUDARYX.Updater.csproj"
$installerScript = Join-Path $ProjectRoot "LUDARYX-Installer-$ExpectedVersion.iss"
$releaseNotes = Join-Path $ProjectRoot "RELEASE-$ExpectedVersion.txt"
$publishScript = Join-Path $PSScriptRoot "Publish-Release.ps1"

Require-File $mainProject
Require-File $updaterProject
Require-File $installerScript
Require-File $releaseNotes
Require-File $publishScript

[xml]$mainXml = Get-Content -LiteralPath $mainProject -Raw
[xml]$updaterXml = Get-Content -LiteralPath $updaterProject -Raw

$mainVersion = [string]$mainXml.Project.PropertyGroup.Version
$mainAssemblyVersion = [string]$mainXml.Project.PropertyGroup.AssemblyVersion
$mainFileVersion = [string]$mainXml.Project.PropertyGroup.FileVersion
$mainInformationalVersion = [string]$mainXml.Project.PropertyGroup.InformationalVersion
$updaterVersion = [string]$updaterXml.Project.PropertyGroup.Version

Require-Equal "Versão do LUDARYX" $mainVersion $ExpectedVersion
Require-Equal "AssemblyVersion do LUDARYX" $mainAssemblyVersion "$ExpectedVersion.0"
Require-Equal "FileVersion do LUDARYX" $mainFileVersion "$ExpectedVersion.0"
Require-Equal "InformationalVersion do LUDARYX" $mainInformationalVersion $ExpectedVersion
Require-Equal "Versão do Updater" $updaterVersion $ExpectedVersion

$iss = Get-Content -LiteralPath $installerScript -Raw
if ($iss -notmatch [regex]::Escape("#define MyAppVersion `"$ExpectedVersion`"")) {
    throw "O instalador não declara MyAppVersion $ExpectedVersion."
}
if ($iss -notmatch [regex]::Escape("OutputBaseFilename=LUDARYX-$ExpectedVersion-Setup")) {
    throw "O nome de saída do instalador não corresponde à versão $ExpectedVersion."
}
if ($iss -notmatch [regex]::Escape("VersionInfoVersion=$ExpectedVersion.0")) {
    throw "VersionInfoVersion do instalador não corresponde à versão $ExpectedVersion."
}
Write-Host "[OK] Instalador Inno Setup consistente" -ForegroundColor Green

$releaseText = Get-Content -LiteralPath $releaseNotes -Raw
if ($releaseText -notmatch [regex]::Escape("LUDARYX $ExpectedVersion")) {
    throw "As notas de release não mencionam LUDARYX $ExpectedVersion."
}
Write-Host "[OK] RELEASE-$ExpectedVersion.txt encontrado e consistente" -ForegroundColor Green

$publishText = Get-Content -LiteralPath $publishScript -Raw
if ($publishText -notmatch [regex]::Escape("LUDARYX-Installer-$ExpectedVersion.iss")) {
    throw "Publish-Release.ps1 não aponta para o instalador $ExpectedVersion."
}
Write-Host "[OK] Script de publicação aponta para $ExpectedVersion" -ForegroundColor Green

$obsoleteIss = Get-ChildItem -LiteralPath $ProjectRoot -Filter "LUDARYX-Installer-*.iss" -File |
    Where-Object { $_.Name -ne "LUDARYX-Installer-$ExpectedVersion.iss" }
if ($obsoleteIss) {
    $names = ($obsoleteIss.Name -join ", ")
    throw "Há instaladores .iss de versões antigas na raiz: $names"
}

Write-Host ""
Write-Host "Validação da release $ExpectedVersion concluída com sucesso." -ForegroundColor Green
