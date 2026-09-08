# Provisiona os modelos ONNX fixados nos manifestos de captcha.
# Uso:
#   ./tools/Get-CaptchaModels.ps1 [-Destination <pasta>] [-Force] [-VerifyOnly]
#   ./tools/Get-CaptchaModels.ps1 -Suite hcaptcha [-ModelId <id|*>] [-Destination <pasta>] [-ManifestPath <arquivo>] [-LicensePath <arquivo>] [-Force] [-VerifyOnly]
param(
    [ValidateSet("ocr", "hcaptcha")]
    [string]$Suite = "ocr",
    [string]$Destination,
    [string]$ManifestPath,
    [string]$LicensePath,
    [string]$ModelId,
    [switch]$Force,
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if (-not $Destination) {
    $Destination = $Suite -eq "hcaptcha" `
        ? (Join-Path $repositoryRoot "captcha-models\hcaptcha") `
        : (Join-Path $repositoryRoot "captcha-models")
}
if (-not $ManifestPath) {
    $manifestName = $Suite -eq "hcaptcha" `
        ? "hcaptcha-models.manifest.json" `
        : "captcha-models.manifest.json"
    $ManifestPath = Join-Path $repositoryRoot "src\RpaFlow.Playwright\V2\Captcha\$manifestName"
}
if (-not $ModelId) {
    $ModelId = $Suite -eq "hcaptcha" ? "*" : "ddddocr-common-old-1.4.11"
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
if (-not [System.IO.File]::Exists($manifestFullPath)) {
    throw "model_missing: manifesto não encontrado em '$manifestFullPath'."
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "model_mismatch: versão de manifesto não suportada: '$($manifest.schemaVersion)'."
}

$destinationPath = [System.IO.Path]::GetFullPath($Destination)

if ($Suite -eq "hcaptcha" -and -not $LicensePath) {
    $LicensePath = Join-Path (
        [System.IO.Path]::GetDirectoryName($manifestFullPath)) "hcaptcha-models.LICENSE.txt"
}

function Publish-VerifiedArtifact {
    param(
        [Parameter(Mandatory = $true)]$Model,
        [Parameter(Mandatory = $true)][string]$TemporaryZip
    )

    if ((Get-Item -LiteralPath $TemporaryZip).Length -ne [long]$Model.source.artifactSizeBytes) {
        throw "model_mismatch: tamanho do artefato baixado diverge do manifesto."
    }
    if ((Get-Sha256 $TemporaryZip) -ne $Model.source.artifactSha256.ToUpperInvariant()) {
        throw "model_mismatch: SHA-256 do artefato baixado diverge do manifesto."
    }
}

function Publish-ModelFile {
    param(
        [Parameter(Mandatory = $true)][string]$ExtractedModel,
        [Parameter(Mandatory = $true)]$Model,
        [Parameter(Mandatory = $true)][string]$Target
    )

    if ((Get-Item -LiteralPath $ExtractedModel).Length -ne [long]$Model.modelSizeBytes) {
        throw "model_mismatch: tamanho do modelo extraído diverge do manifesto."
    }
    if ((Get-Sha256 $ExtractedModel) -ne $Model.modelSha256.ToUpperInvariant()) {
        throw "model_mismatch: SHA-256 do modelo extraído diverge do manifesto."
    }

    $temporaryTarget = Join-Path $destinationPath (
        ".$($Model.publishedFile).$([Guid]::NewGuid().ToString('N')).tmp")
    try {
        Copy-Item -LiteralPath $ExtractedModel -Destination $temporaryTarget
        if ([System.IO.File]::Exists($Target)) {
            $backupName = "$($Model.publishedFile).unpinned-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss')).bak"
            $backupPath = Join-Path $destinationPath $backupName
            [System.IO.File]::Replace($temporaryTarget, $Target, $backupPath)
            Write-Host "Modelo anterior preservado como $backupName."
        }
        else {
            [System.IO.File]::Move($temporaryTarget, $Target)
        }
        Write-Host "model_ready: $Target ($($Model.modelSha256))"
    }
    finally {
        Remove-Item -LiteralPath $temporaryTarget -Force -ErrorAction SilentlyContinue
    }
}

function Confirm-CachedModel {
    param(
        [Parameter(Mandatory = $true)]$Model,
        [Parameter(Mandatory = $true)][string]$Target
    )

    if (-not [System.IO.File]::Exists($Target)) {
        if ($VerifyOnly) {
            throw "model_missing: modelo não encontrado em '$Target'."
        }
        return $false
    }

    $currentHash = Get-Sha256 $Target
    if ($currentHash -eq $Model.modelSha256.ToUpperInvariant()) {
        Write-Host "cache_valid: $Target ($currentHash)"
        return $true
    }

    if ($VerifyOnly -or -not $Force) {
        throw "model_mismatch: '$Target' tem SHA-256 $currentHash; esperado $($Model.modelSha256). Use -Force para substituir preservando backup."
    }
    return $false
}

if (-not (Test-Path -LiteralPath $destinationPath)) {
    New-Item -ItemType Directory -Path $destinationPath | Out-Null
}

if ($Suite -eq "ocr") {
    $model = @($manifest.models) | Where-Object { $_.id -eq $ModelId }
    if ($model.Count -ne 1) {
        throw "model_missing: o manifesto não possui exatamente um modelo '$ModelId'."
    }
    $model = $model[0]

    $charsetPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $model.charset.path))
    if (-not [System.IO.File]::Exists($charsetPath)) {
        throw "model_missing: charset não encontrado em '$charsetPath'."
    }
    if ((Get-Sha256 $charsetPath) -ne $model.charset.sha256.ToUpperInvariant()) {
        throw "model_mismatch: o charset '$charsetPath' não corresponde ao manifesto."
    }

    $target = Join-Path $destinationPath $model.publishedFile
    if (Confirm-CachedModel -Model $model -Target $target) {
        return
    }

    $temporaryRoot = Join-Path $destinationPath ".captcha-model-$([Guid]::NewGuid().ToString('N'))"
    $temporaryZip = Join-Path $temporaryRoot "artifact.zip"
    $temporaryExtract = Join-Path $temporaryRoot "extracted"
    try {
        New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
        Write-Host "Baixando artefato fixado $($model.source.package) $($model.source.packageVersion)..."
        Invoke-WebRequest -Uri $model.source.artifactUrl -OutFile $temporaryZip -MaximumRedirection 0
        Publish-VerifiedArtifact -Model $model -TemporaryZip $temporaryZip

        Expand-Archive -LiteralPath $temporaryZip -DestinationPath $temporaryExtract -Force
        $relativeInnerPath = $model.source.innerPath.Replace(
            '/', [System.IO.Path]::DirectorySeparatorChar)
        $extractedModel = Join-Path $temporaryExtract $relativeInnerPath
        if (-not [System.IO.File]::Exists($extractedModel)) {
            throw "model_missing: '$($model.source.innerPath)' não existe no artefato fixado."
        }
        Publish-ModelFile -ExtractedModel $extractedModel -Model $model -Target $target
    }
    finally {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    return
}

# Suite hcaptcha: classificadores binários ResNet do model hub (download direto).
if ($manifest.suite -ne "hcaptcha-resnet-binary") {
    throw "model_mismatch: o manifesto '$manifestFullPath' não é da suíte hcaptcha-resnet-binary."
}

$models = @($manifest.models)
if ($ModelId -ne "*") {
    $models = @($models | Where-Object { $_.id -eq $ModelId })
    if ($models.Count -ne 1) {
        throw "model_missing: o manifesto não possui exatamente um modelo '$ModelId'."
    }
}

foreach ($model in $models) {
    $target = Join-Path $destinationPath $model.publishedFile
    if (Confirm-CachedModel -Model $model -Target $target) {
        continue
    }

    $temporaryRoot = Join-Path $destinationPath ".captcha-model-$([Guid]::NewGuid().ToString('N'))"
    $temporaryDownload = Join-Path $temporaryRoot "artifact.onnx"
    try {
        New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
        Write-Host "Baixando modelo fixado $($model.id) do model hub..."
        # O model hub publica assets com redirect assinado para a CDN do GitHub;
        # a integridade continua garantida pelo SHA-256 fixado no manifesto.
        Invoke-WebRequest -Uri $model.artifactUrl -OutFile $temporaryDownload -MaximumRedirection 3
        if ((Get-Item -LiteralPath $temporaryDownload).Length -ne [long]$model.modelSizeBytes) {
            throw "model_mismatch: tamanho do modelo baixado diverge do manifesto."
        }
        Publish-ModelFile -ExtractedModel $temporaryDownload -Model $model -Target $target
    }
    finally {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$licenseFullPath = [System.IO.Path]::GetFullPath($LicensePath)
if (-not [System.IO.File]::Exists($licenseFullPath)) {
    throw "model_missing: licença dos modelos não encontrada em '$licenseFullPath'."
}
$licenseTarget = Join-Path $destinationPath "LICENSE"
if ($VerifyOnly) {
    if (-not [System.IO.File]::Exists($licenseTarget) -or
        (Get-Sha256 $licenseTarget) -ne (Get-Sha256 $licenseFullPath)) {
        throw "model_mismatch: licença provisionada não corresponde a '$licenseFullPath'."
    }
    Write-Host "cache_valid: $licenseTarget"
}
else {
    Copy-Item -LiteralPath $licenseFullPath -Destination $licenseTarget -Force
    Write-Host "license_ready: $licenseTarget"
}
