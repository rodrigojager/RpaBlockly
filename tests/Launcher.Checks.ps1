$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$launcher = Join-Path $repositoryRoot "tools\RpaBlockly.ps1"
$temporaryRoot = Join-Path $repositoryRoot (
    "tests\.launcher-checks-$([Guid]::NewGuid().ToString('N'))")
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false, $true)

function Invoke-Launcher(
    [string[]]$Arguments,
    [bool]$ShouldSucceed,
    [string]$ExpectedText) {
    $output = (& pwsh -NoProfile -File $launcher @Arguments 2>&1) -join "`n"
    $succeeded = $LASTEXITCODE -eq 0
    if ($succeeded -ne $ShouldSucceed) {
        throw "Launcher retornou sucesso=$succeeded; esperado=$ShouldSucceed.`n$output"
    }
    if (-not $output.Contains($ExpectedText, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Launcher não retornou '$ExpectedText'.`n$output"
    }
}

function Write-TestProfile([string]$ConfigurationFile) {
    $profile = [ordered]@{
        displayName = "Launcher fixture"
        projectFile = "Fixture.csproj"
        configurationFile = $ConfigurationFile
        rpaId = "launcher-fixture"
        packageStoreRoot = "package-store"
        configurationFields = @()
    } | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText(
        (Join-Path $temporaryRoot "rpa.editor.json"),
        $profile.ReplaceLineEndings("`n") + "`n",
        $utf8WithoutBom)
}

try {
    Invoke-Launcher @("doctor") $true "Diagnóstico"

    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $temporaryRoot "Fixture.csproj"),
        '<Project Sdk="Microsoft.NET.Sdk" />' + "`n",
        $utf8WithoutBom)
    Write-TestProfile "appsettings.local.json"
    $relativeFixture = [System.IO.Path]::GetRelativePath(
        $repositoryRoot,
        $temporaryRoot)
    Invoke-Launcher @("doctor", "-Project", $relativeFixture) $true "será criada"

    Write-TestProfile "..\..\README.md"
    Invoke-Launcher @("doctor", "-Project", $relativeFixture) $false "permanecer dentro"

    Write-TestProfile "settings.production.json"
    Invoke-Launcher @("doctor", "-Project", $relativeFixture) $false "settings.production.json"

    Write-Host "Launcher local validado com sucesso."
}
finally {
    if ([System.IO.Directory]::Exists($temporaryRoot)) {
        [System.IO.Directory]::Delete($temporaryRoot, $true)
    }
}
