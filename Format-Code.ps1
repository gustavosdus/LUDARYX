param(
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'O .NET SDK não foi encontrado no PATH.'
}

if ($Verify) {
    dotnet format --verify-no-changes
    exit $LASTEXITCODE
}

dotnet format
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet build -c Release
exit $LASTEXITCODE
