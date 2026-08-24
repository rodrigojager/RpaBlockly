$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    $raw = dotnet list RpaBlockly.slnx package --vulnerable --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw 'A análise de dependências NuGet falhou.' }
    $inventory = $raw | ConvertFrom-Json
    $vulnerabilities = @()
    foreach ($project in $inventory.projects) {
        foreach ($framework in $project.frameworks) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                foreach ($vulnerability in @($package.vulnerabilities)) {
                    if ($null -ne $vulnerability) {
                        $vulnerabilities += "$($package.id) $($package.resolvedVersion): $($vulnerability.severity) $($vulnerability.advisoryurl)"
                    }
                }
            }
        }
    }
    if ($vulnerabilities.Count -gt 0) {
        throw "Dependências vulneráveis encontradas:`n$($vulnerabilities -join "`n")"
    }
    Write-Output 'Nenhuma vulnerabilidade NuGet conhecida foi reportada.'

    foreach ($npmProject in @('tools/schema-conformance', 'src/RpaFlow.Recorder.Extension')) {
        npm audit --audit-level=high --ignore-scripts --no-fund --prefix $npmProject
        if ($LASTEXITCODE -ne 0) {
            throw "A análise de dependências npm encontrou vulnerabilidade alta ou crítica em $npmProject."
        }
    }
    Write-Output 'Nenhuma vulnerabilidade npm alta ou crítica foi reportada.'
}
finally {
    Pop-Location
}
