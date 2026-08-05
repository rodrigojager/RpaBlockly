$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

Push-Location $repositoryRoot
try {
    dotnet build RpaBlockly.slnx
    if ($LASTEXITCODE -ne 0) {
        throw "Falha na compilação da solução."
    }

    dotnet run --project tests/RpaBase.Checks/RpaBase.Checks.csproj --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "Falha nas verificações da base."
    }

    dotnet run --project tests/Rpa.WorkerChecks/Rpa.WorkerChecks.csproj --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "Falha nas verificações do worker e do provider de OTP."
    }

    dotnet run --project tests/RpaFlow.PlaywrightChecks/RpaFlow.PlaywrightChecks.csproj --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "Falha nas verificações locais do Playwright."
    }

    dotnet run --project examples/RpaExemplo/RpaExemplo.csproj --no-build -- --validate-only
    if ($LASTEXITCODE -ne 0) {
        throw "Falha na validação do RPA de exemplo."
    }

    dotnet run --project src/Rpa.Worker/Rpa.Worker.csproj --no-build -- --validate-only
    if ($LASTEXITCODE -ne 0) {
        throw "Falha na validação segura do worker."
    }

    node --check docs/assets/block-catalog.js
    node --check docs/assets/manual.js
    node --check docs/manual.config.js
    if ($LASTEXITCODE -ne 0) {
        throw "Falha na validação JavaScript do manual."
    }

    $editorDll = Join-Path $repositoryRoot "src\RpaFlow.Editor\bin\Debug\net9.0\RpaFlow.Editor.dll"
    dotnet run --project tests/RpaFlow.EditorRoundTrip/RpaFlow.EditorRoundTrip.csproj --no-build -- `
      $editorDll $repositoryRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Falha no round-trip do editor Blockly."
    }

    Write-Output "Base compilada e validada com sucesso."
}
finally {
    Pop-Location
}
