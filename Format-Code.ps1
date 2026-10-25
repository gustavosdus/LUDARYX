param(
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$projects = @(
    (Join-Path $PSScriptRoot 'UnifiedGameLauncher.csproj'),
    (Join-Path $PSScriptRoot 'Updater/LUDARYX.Updater.csproj'),
    (Join-Path $PSScriptRoot 'Tests/LUDARYX.RegressionTests.csproj')
)

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'O .NET SDK não foi encontrado no PATH.'
}

foreach ($project in $projects) {
    if ($Verify) {
        dotnet format $project --verify-no-changes
    }
    else {
        dotnet format $project
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao formatar/verificar $project."
    }
}

if (-not $Verify) {
    foreach ($project in $projects) {
        dotnet build $project -c Release
        if ($LASTEXITCODE -ne 0) {
            throw "Falha ao compilar $project."
        }
    }
}
