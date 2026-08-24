param(
    [string]$OutputPath = 'artifacts/sbom.spdx.json'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    $raw = dotnet list RpaBlockly.slnx package --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível inventariar pacotes NuGet.' }
    $inventory = $raw | ConvertFrom-Json
    $packages = @{}
    foreach ($project in $inventory.projects) {
        foreach ($framework in $project.frameworks) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($null -eq $package -or [string]::IsNullOrWhiteSpace($package.id)) { continue }
                $key = "nuget:$($package.id)@$($package.resolvedVersion)"
                $packages[$key] = [ordered]@{
                    SPDXID = 'SPDXRef-Package-NuGet-' + ($key -replace '[^A-Za-z0-9.-]', '-')
                    name = $package.id
                    versionInfo = $package.resolvedVersion
                    downloadLocation = 'NOASSERTION'
                    filesAnalyzed = $false
                    licenseConcluded = 'NOASSERTION'
                    licenseDeclared = 'NOASSERTION'
                    supplier = 'NOASSERTION'
                }
            }
        }
    }

    $npmLockPaths = @(
        'tools/schema-conformance/package-lock.json',
        'src/RpaFlow.Recorder.Extension/package-lock.json'
    )
    foreach ($relativeNpmLockPath in $npmLockPaths) {
        $npmLockPath = Join-Path $repositoryRoot $relativeNpmLockPath
        $npmInventory = & node -e "const fs=require('fs');const lock=JSON.parse(fs.readFileSync(process.argv[1],'utf8'));const rows=Object.entries(lock.packages).filter(([path,value])=>path&&value.version).map(([path,value])=>({path,...value}));process.stdout.write(JSON.stringify(rows));" $npmLockPath
        if ($LASTEXITCODE -ne 0) { throw "Não foi possível ler $relativeNpmLockPath." }
        $npmPackages = $npmInventory | ConvertFrom-Json
        foreach ($entry in $npmPackages) {
            if ([string]::IsNullOrWhiteSpace($entry.path) -or
                [string]::IsNullOrWhiteSpace($entry.version)) {
                continue
            }
            $name = $entry.name
            if ([string]::IsNullOrWhiteSpace($name)) {
                $name = $entry.path -replace '^node_modules/', ''
            }
            $key = "npm:$name@$($entry.version)"
            $declaredLicense = if ([string]::IsNullOrWhiteSpace($entry.license)) {
                'NOASSERTION'
            } else {
                $entry.license
            }
            $downloadLocation = if ([string]::IsNullOrWhiteSpace($entry.resolved)) {
                'NOASSERTION'
            } else {
                $entry.resolved
            }
            $packages[$key] = [ordered]@{
                SPDXID = 'SPDXRef-Package-Npm-' + ($key -replace '[^A-Za-z0-9.-]', '-')
                name = $name
                versionInfo = $entry.version
                downloadLocation = $downloadLocation
                filesAnalyzed = $false
                licenseConcluded = 'NOASSERTION'
                licenseDeclared = $declaredLicense
                supplier = 'NOASSERTION'
            }
        }
    }

    $document = [ordered]@{
        spdxVersion = 'SPDX-2.3'
        dataLicense = 'CC0-1.0'
        SPDXID = 'SPDXRef-DOCUMENT'
        name = 'RpaBlockly-2.0.0-rc.1'
        documentNamespace = 'https://rpablockly.local/sbom/2.0.0-rc.1'
        creationInfo = [ordered]@{
            created = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
            creators = @('Tool: tools/Generate-Sbom.ps1')
        }
        packages = @($packages.Values | Sort-Object name, versionInfo)
    }
    $fullOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
    $allowed = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
    if (-not $fullOutput.StartsWith($allowed + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'O SBOM deve ser gravado dentro de artifacts/.'
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
    $json = ($document | ConvertTo-Json -Depth 12) -replace "`r`n", "`n"
    [IO.File]::WriteAllText($fullOutput, $json + "`n", [Text.UTF8Encoding]::new($false, $true))
    Write-Output $fullOutput
}
finally {
    Pop-Location
}
