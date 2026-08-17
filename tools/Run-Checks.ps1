param(
    [switch]$IncludeSql
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    dotnet build RpaBlockly.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'A compilação da solução falhou.' }

    dotnet build templates/rpa-web/RpaTemplate.csproj --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'A compilação do template falhou.' }

    npm ci --ignore-scripts --no-audit --no-fund --prefix tools/schema-conformance
    if ($LASTEXITCODE -ne 0) { throw 'A restauração dos checks TypeScript falhou.' }
    npm run check --prefix tools/schema-conformance
    if ($LASTEXITCODE -ne 0) { throw 'A conformidade TypeScript dos schemas falhou.' }

    npm ci --ignore-scripts --no-audit --no-fund --prefix src/RpaFlow.Recorder.Extension
    if ($LASTEXITCODE -ne 0) { throw 'A restauração da extensão Recorder falhou.' }
    npm run check --prefix src/RpaFlow.Recorder.Extension
    if ($LASTEXITCODE -ne 0) { throw 'Os checks da extensão Recorder falharam.' }
    npm run licenses --prefix src/RpaFlow.Recorder.Extension -- --verify
    if ($LASTEXITCODE -ne 0) { throw 'O inventário de licenças do Recorder está desatualizado.' }
    npm run release --prefix src/RpaFlow.Recorder.Extension -- --verify
    if ($LASTEXITCODE -ne 0) { throw 'O build reproduzível do Recorder divergiu.' }

    $checks = @(
        'tests/RpaFlow.ContractsChecks/RpaFlow.ContractsChecks.csproj',
        'tests/RpaFlow.PackagesChecks/RpaFlow.PackagesChecks.csproj',
        'tests/RpaFlow.MigratorChecks/RpaFlow.MigratorChecks.csproj',
        'tests/Rpa.WorkerChecks/Rpa.WorkerChecks.csproj',
        'tests/RpaBase.Checks/RpaBase.Checks.csproj',
        'tests/RpaFlow.PlaywrightChecks/RpaFlow.PlaywrightChecks.csproj',
        'tests/RpaFlow.RecorderContractChecks/RpaFlow.RecorderContractChecks.csproj'
    )
    foreach ($project in $checks) {
        dotnet run --project $project --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "O check $project falhou." }
    }


    $editorDll = Join-Path $repositoryRoot 'src/RpaFlow.Editor/bin/Release/net9.0/RpaFlow.Editor.dll'
    dotnet run --project tests/RpaFlow.EditorRoundTrip/RpaFlow.EditorRoundTrip.csproj --configuration Release --no-build -- $editorDll $repositoryRoot
    if ($LASTEXITCODE -ne 0) { throw 'O check do editor V2 falhou.' }

    if ($IncludeSql) {
        $env:RPABLOCKLY_REQUIRE_SQL_TESTS = 'true'
        dotnet run --project tests/RpaFlow.Packages.SqlServerChecks/RpaFlow.Packages.SqlServerChecks.csproj --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'O check SQL Server falhou.' }
    }
}
finally {
    Pop-Location
}
