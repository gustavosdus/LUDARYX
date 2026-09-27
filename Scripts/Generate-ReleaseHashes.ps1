param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$items = Get-ChildItem -LiteralPath $Path -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" }
if (-not $items) {
    throw "Nenhum arquivo encontrado em: $Path"
}

$items | ForEach-Object {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $_.Name
} | Set-Content -Encoding ASCII (Join-Path $Path "SHA256SUMS.txt")

Write-Host "SHA256SUMS.txt criado em $Path"
