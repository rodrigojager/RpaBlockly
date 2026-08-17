param(
    [switch]$IncludeSql
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "Run-Checks.ps1") -IncludeSql:$IncludeSql
if ($LASTEXITCODE -ne 0) {
    throw "Falha na suíte completa da base V2."
}

Write-Output "Base V2 compilada e validada com sucesso."
