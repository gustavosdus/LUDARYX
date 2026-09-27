param(
    [string]$Version = "1.0.2",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ValidateScript = Join-Path $PSScriptRoot "Validate-Release.ps1"
$PublishScript = Join-Path $PSScriptRoot "Publish-Release.ps1"
$HashScript = Join-Path $PSScriptRoot "Generate-ReleaseHashes.ps1"
$InstallerScript = Join-Path $ProjectRoot "LUDARYX-Installer-$Version.iss"
$InstallerDir = Join-Path $ProjectRoot "Installer"
$InstallerName = "LUDARYX-$Version-Setup.exe"
$InstallerPath = Join-Path $InstallerDir $InstallerName

& $ValidateScript -ExpectedVersion $Version

$runningLudaryx = Get-Process -Name "LUDARYX" -ErrorAction SilentlyContinue
if ($runningLudaryx) {
    $processList = ($runningLudaryx | ForEach-Object { "PID $($_.Id)" }) -join ", "
    throw "O LUDARYX está aberto ($processList). Encerre-o completamente, inclusive pela System Tray, antes de gerar a release."
}

& $PublishScript -Runtime $Runtime -Configuration $Configuration

$programFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
$programFiles = [Environment]::GetFolderPath("ProgramFiles")
$isccCandidates = @(
    (Join-Path $programFiles "Inno Setup 7\ISCC.exe"),
    (Join-Path $programFilesX86 "Inno Setup 7\ISCC.exe"),
    (Join-Path $programFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup não foi encontrado. O script procura automaticamente pelas versões 7 e 6. Compile manualmente se necessário: $InstallerScript"
}

Write-Host "Inno Setup encontrado em: $iscc" -ForegroundColor Green

Write-Host ""
Write-Host "Compilando instalador LUDARYX $Version..." -ForegroundColor Cyan
& $iscc $InstallerScript
if ($LASTEXITCODE -ne 0) {
    throw "A compilação do instalador falhou."
}

if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "O instalador esperado não foi criado: $InstallerPath"
}

& $HashScript -Path $InstallerDir -FileName $InstallerName

Write-Host ""
Write-Host "Release local pronta para upload:" -ForegroundColor Green
Write-Host "  $InstallerPath"
Write-Host "  $(Join-Path $InstallerDir 'SHA256SUMS.txt')"
Write-Host ""
Write-Host "Use RELEASE-$Version.txt como base para as notas da GitHub Release."
