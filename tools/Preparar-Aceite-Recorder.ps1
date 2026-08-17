param(
    [string]$Destination = "tmp/recorder-acceptance",

    [ValidateRange(1024, 65535)]
    [int]$FixturePort = 5178
)

$ErrorActionPreference = "Stop"
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false, $true)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$destinationPath = if ([System.IO.Path]::IsPathRooted($Destination)) {
    [System.IO.Path]::GetFullPath($Destination)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Destination))
}
$allowedPrefix = $repositoryRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $destinationPath.StartsWith(
    $allowedPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "O destino do aceite precisa permanecer dentro do repositório."
}

if (Test-Path -LiteralPath $destinationPath) {
    throw "O destino já existe: $destinationPath. Use outra pasta para preservar a execução anterior."
}

$sourcePackageStore = Join-Path $repositoryRoot "examples/RpaExemplo/package-store"
New-Item -ItemType Directory -Path $destinationPath | Out-Null
Copy-Item -LiteralPath $sourcePackageStore -Destination $destinationPath -Recurse

$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>
'@
[System.IO.File]::WriteAllText(
    (Join-Path $destinationPath "RecorderAcceptance.csproj"),
    $project.Replace("`r`n", "`n"),
    $utf8WithoutBom)

$profile = [ordered]@{
    displayName = "Aceite independente do Recorder V2"
    projectFile = "RecorderAcceptance.csproj"
    configurationFile = "appsettings.local.json"
    rpaId = "rpa-exemplo"
    packageStoreRoot = "package-store"
    configurationFields = @(
        [ordered]@{ path = "Input.Url"; label = "URL inicial da fixture"; source = "input.url"; type = "url" },
        [ordered]@{ path = "Input.Nome"; label = "Nome de teste"; source = "input.nome"; type = "text" },
        [ordered]@{ path = "Input.Estado"; label = "Estado de teste"; source = "input.estado"; type = "text" },
        [ordered]@{ path = "Input.Aceite"; label = "Checkbox de aceite"; source = "input.aceite"; type = "checkbox" },
        [ordered]@{ path = "Attachments.Arquivo"; label = "Arquivo sanitizado"; source = "attachments.arquivo"; type = "text" },
        [ordered]@{ path = "Runtime.Headless"; label = "Executar sem interface"; type = "checkbox" },
        [ordered]@{ path = "Runtime.Browser"; label = "Navegador Playwright"; type = "text" }
    )
}
$configuration = [ordered]@{
    Runtime = [ordered]@{
        Headless = $false
        Browser = "chromium"
        Locale = "pt-BR"
        ViewportWidth = 1440
        ViewportHeight = 1000
        ActionTimeoutSeconds = 30
        UploadTimeoutSeconds = 90
        ReadinessQuietPeriodMs = 100
        FormStabilityMs = 100
        BusySelectors = @("[aria-busy='true']", "[data-loading='true']", ".loading", ".spinner")
        OutputDirectory = "artifacts"
        PackageStoreRoot = "package-store"
        RpaId = "rpa-exemplo"
        StorageStatePath = $null
        SaveStorageState = $false
        HoldBrowserOpenForInspection = $false
        MaximumArtifactBytes = 52428800
        MaximumArtifactFilesPerExecution = 100
        ArtifactRetentionDays = 30
    }
    Input = [ordered]@{
        Url = "http://127.0.0.1:$FixturePort/index.html"
        Nome = "Maria da Silva"
        Estado = "SP"
        Aceite = $true
    }
    Attachments = [ordered]@{
        Arquivo = "arquivo-aceite.txt"
    }
    Blockly = [ordered]@{
        Variables = [ordered]@{}
    }
}

foreach ($document in @(
    @{ Name = "rpa.editor.json"; Value = $profile },
    @{ Name = "appsettings.local.json"; Value = $configuration }
)) {
    $json = $document.Value | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText(
        (Join-Path $destinationPath $document.Name),
        $json.Replace("`r`n", "`n") + "`n",
        $utf8WithoutBom)
}

[System.IO.File]::WriteAllText(
    (Join-Path $destinationPath "arquivo-aceite.txt"),
    "Arquivo sanitizado para o aceite independente do Recorder V2.`n",
    $utf8WithoutBom)

Write-Output "Área descartável criada em: $destinationPath"
Write-Output "Fixture: dotnet run --project tools/RpaFlow.RecorderFixture"
Write-Output "Editor: dotnet run --project src/RpaFlow.Editor -- --project-root `"$destinationPath`""
Write-Output "Host: dotnet run --project examples/RpaExemplo -- --config `"$destinationPath/appsettings.local.json`" --package-store `"$destinationPath/package-store`" --rpa-id rpa-exemplo"
