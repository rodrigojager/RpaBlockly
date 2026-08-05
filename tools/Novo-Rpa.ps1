param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9._-]*$')]
    [string]$Name,

    [string]$DisplayName,

    [string]$DestinationRoot = "rpas",

    [switch]$DoNotAddToSolution
)

$ErrorActionPreference = "Stop"
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false, $true)

function Get-RelativePathPortable(
    [string]$fromDirectory,
    [string]$targetPath) {
    $fromFullPath = [System.IO.Path]::GetFullPath($fromDirectory).TrimEnd('\') + '\'
    $targetFullPath = [System.IO.Path]::GetFullPath($targetPath)
    $fromUri = [System.Uri]::new($fromFullPath)
    $targetUri = [System.Uri]::new($targetFullPath)
    $relativeUri = $fromUri.MakeRelativeUri($targetUri)
    return [System.Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', '\')
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$templateRoot = Join-Path $repositoryRoot "templates\rpa-web"
$destinationRootPath = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $DestinationRoot))
$destination = [System.IO.Path]::GetFullPath(
    (Join-Path $destinationRootPath $Name))
$allowedPrefix = $repositoryRoot.TrimEnd('\') + '\'

if (-not $destination.StartsWith(
    $allowedPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "O destino precisa permanecer dentro do repositório: $repositoryRoot"
}

if (Test-Path -LiteralPath $destination) {
    throw "O destino já existe: $destination"
}

if ([string]::IsNullOrWhiteSpace($DisplayName)) {
    $DisplayName = $Name
}

New-Item -ItemType Directory -Path $destinationRootPath -Force | Out-Null
Copy-Item -LiteralPath $templateRoot -Destination $destination -Recurse

$templateProject = Join-Path $destination "RpaTemplate.csproj"
$projectFile = Join-Path $destination "$Name.csproj"
Move-Item -LiteralPath $templateProject -Destination $projectFile

$relativeSource = Get-RelativePathPortable `
    $destination `
    (Join-Path $repositoryRoot "src")
$filesToCustomize = @(
    $projectFile,
    (Join-Path $destination "rpa.editor.json"),
    (Join-Path $destination "flow.production.json"),
    (Join-Path $destination "README.md")
)

foreach ($file in $filesToCustomize) {
    $content = [System.IO.File]::ReadAllText($file, $utf8WithoutBom)
    $content = $content.Replace(
        "rpas\RpaTemplate",
        (Join-Path $DestinationRoot $Name))
    $content = $content.Replace("RpaTemplate", $Name)
    $content = $content.Replace("Novo RPA web", $DisplayName)
    $content = $content.Replace("..\..\src", $relativeSource)
    [System.IO.File]::WriteAllText($file, $content, $utf8WithoutBom)
}

$exampleConfiguration = Join-Path $destination "appsettings.example.json"
$localConfiguration = Join-Path $destination "appsettings.local.json"
Copy-Item -LiteralPath $exampleConfiguration -Destination $localConfiguration

if (-not $DoNotAddToSolution) {
    $solution = Join-Path $repositoryRoot "RpaBlockly.slnx"
    & dotnet sln $solution add $projectFile
    if ($LASTEXITCODE -ne 0) {
        throw "O projeto foi criado, mas não pôde ser adicionado à solução."
    }
}

Write-Output "RPA criado em: $destination"
Write-Output "1. Edite appsettings.local.json."
Write-Output "2. Execute: dotnet run --project `"$projectFile`" -- --validate-only"
Write-Output "3. Abra o editor: .\abrir-editor.cmd `"$DestinationRoot\$Name`""
