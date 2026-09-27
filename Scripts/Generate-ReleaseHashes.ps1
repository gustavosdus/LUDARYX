param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [string]$FileName
)

$ErrorActionPreference = "Stop"
$resolvedPath = (Resolve-Path -LiteralPath $Path).Path

if ([string]::IsNullOrWhiteSpace($FileName)) {
    $items = Get-ChildItem -LiteralPath $resolvedPath -File |
        Where-Object { $_.Name -ne "SHA256SUMS.txt" }
}
else {
    $target = Join-Path $resolvedPath $FileName
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "Arquivo não encontrado para gerar SHA-256: $target"
    }
    $items = @(Get-Item -LiteralPath $target)
}

if (-not $items) {
    throw "Nenhum arquivo encontrado em: $resolvedPath"
}

$output = Join-Path $resolvedPath "SHA256SUMS.txt"
$items | ForEach-Object {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $_.Name
} | Set-Content -Encoding ASCII $output

Write-Host "SHA256SUMS.txt criado em $resolvedPath" -ForegroundColor Green
Get-Content -LiteralPath $output
