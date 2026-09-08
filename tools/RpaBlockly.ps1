[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet(
        "menu",
        "doctor",
        "setup",
        "editor",
        "validate",
        "captcha-up",
        "captcha-down",
        "sidecar-up",
        "sidecar-down",
        "status",
        "clean-docker")]
    [string]$Command = "menu",

    [string]$Project = "examples\RpaExemplo",

    [ValidateSet("None", "Models", "Docker")]
    [string]$CaptchaMode = "None",

    [ValidateSet("byparr", "flaresolverr")]
    [string]$SidecarProvider = "byparr",

    [ValidatePattern('^http://127\.0\.0\.1:\d{1,5}$')]
    [string]$Url = "http://127.0.0.1:5187",

    [switch]$NoOpen,
    [switch]$SkipBuild,
    [switch]$SkipBrowserInstall,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false, $true)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solutionPath = Join-Path $repositoryRoot "RpaBlockly.slnx"
$editorProject = Join-Path $repositoryRoot "src\RpaFlow.Editor\RpaFlow.Editor.csproj"
$editorDll = Join-Path $repositoryRoot "src\RpaFlow.Editor\bin\Release\net9.0\RpaFlow.Editor.dll"
$playwrightInstaller = Join-Path $repositoryRoot (
    "src\RpaFlow.Playwright\bin\Release\net9.0\playwright.ps1")
$captchaCompose = Join-Path $repositoryRoot "services\captcha-solver\compose.yaml"
$captchaEnvironment = Join-Path $repositoryRoot "services\captcha-solver\.env.local"
$sidecarCompose = Join-Path $repositoryRoot "services\cloudflare-sidecar\compose.yaml"
if ($null -eq (Get-Command "git" -ErrorAction SilentlyContinue)) {
    throw "Git é obrigatório para iniciar o launcher."
}
$checkoutIdGitPath = (& git -C $repositoryRoot rev-parse --git-path rpablockly-launcher-id).Trim()
if ($LASTEXITCODE -ne 0 -or -not $checkoutIdGitPath) {
    throw "Não foi possível resolver o diretório de metadados do Git."
}
$checkoutIdPath = if ([System.IO.Path]::IsPathRooted($checkoutIdGitPath)) {
    $checkoutIdGitPath
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $checkoutIdGitPath))
}
if (-not [System.IO.File]::Exists($checkoutIdPath)) {
    $candidateId = [Convert]::ToHexString(
        [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(4)).ToLowerInvariant()
    try {
        $stream = [System.IO.FileStream]::new(
            $checkoutIdPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        try {
            $bytes = $utf8WithoutBom.GetBytes("$candidateId`n")
            $stream.Write($bytes, 0, $bytes.Length)
        }
        finally {
            $stream.Dispose()
        }
    }
    catch [System.IO.IOException] {
        if (-not [System.IO.File]::Exists($checkoutIdPath)) {
            throw
        }
    }
}
$checkoutId = [System.IO.File]::ReadAllText(
    $checkoutIdPath,
    $utf8WithoutBom).Trim().ToLowerInvariant()
if ($checkoutId -notmatch '^[a-f0-9]{8}$') {
    throw "Identificador local inválido em '$checkoutIdPath'."
}
$captchaComposeProject = "rpablockly-$checkoutId-captcha"
$sidecarComposeProject = "rpablockly-$checkoutId-sidecar"

function Write-Section([string]$Title) {
    Write-Host ""
    Write-Host "== $Title ==" -ForegroundColor Cyan
}

function Test-Executable([string]$Name) {
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Invoke-Checked(
    [string]$Executable,
    [string[]]$Arguments,
    [string]$FailureMessage) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}

function Assert-NoReparsePoint(
    [string]$Root,
    [string]$Path,
    [string]$Description) {
    $rootFullPath = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar)
    if ([System.IO.File]::Exists($Path) -and
        ([System.IO.File]::GetAttributes($Path) -band
         [System.IO.FileAttributes]::ReparsePoint)) {
        throw "$Description é um link ou junction não permitido: $Path"
    }
    $candidate = if ([System.IO.Directory]::Exists($Path)) {
        [System.IO.DirectoryInfo]::new([System.IO.Path]::GetFullPath($Path))
    }
    else {
        [System.IO.DirectoryInfo]::new(
            [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($Path)))
    }
    while ($null -ne $candidate -and
        -not $candidate.FullName.Equals(
            $rootFullPath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        if ($candidate.Exists -and
            ($candidate.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw "$Description atravessa um link ou junction não permitido: $($candidate.FullName)"
        }
        $candidate = $candidate.Parent
    }
}

function Resolve-InsideDirectory(
    [string]$Root,
    [string]$Path,
    [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Description não foi informado."
    }
    $combined = if ([System.IO.Path]::IsPathRooted($Path)) {
        $Path
    }
    else {
        Join-Path $Root $Path
    }
    $candidate = [System.IO.Path]::GetFullPath($combined)
    $relative = [System.IO.Path]::GetRelativePath($Root, $candidate)
    if ([System.IO.Path]::IsPathRooted($relative) -or
        $relative -eq ".." -or
        $relative.StartsWith(
            "..$([System.IO.Path]::DirectorySeparatorChar)",
            [System.StringComparison]::Ordinal)) {
        throw "$Description precisa permanecer dentro de '$Root'."
    }
    Assert-NoReparsePoint $Root $candidate $Description
    return $candidate
}

function Assert-LocalSecretPath(
    [string]$Path,
    [string]$Description) {
    if (-not (Test-Executable "git")) {
        throw "Git é obrigatório para validar que $Description não será versionado."
    }
    $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $Path)
    & git -C $repositoryRoot rev-parse --is-inside-work-tree 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Não foi possível validar $Description porque a pasta não é um worktree Git."
    }
    & git -C $repositoryRoot ls-files --error-unmatch -- $relative 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        throw "$Description '$Path' já é rastreado pelo Git e não pode receber segredos."
    }
    if ($LASTEXITCODE -ne 1) {
        throw "O Git falhou ao consultar se $Description é rastreado."
    }
    & git -C $repositoryRoot check-ignore --quiet -- $relative
    if ($LASTEXITCODE -ne 0) {
        throw "$Description '$Path' precisa estar ignorado pelo Git antes de receber segredos."
    }
}

function Resolve-ProjectState {
    $candidate = if ([System.IO.Path]::IsPathRooted($Project)) {
        $Project
    }
    else {
        Join-Path $repositoryRoot $Project
    }
    $projectRoot = [System.IO.Path]::GetFullPath($candidate)
    $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $projectRoot)
    if ([System.IO.Path]::IsPathRooted($relative) -or
        $relative -eq ".." -or
        $relative.StartsWith(
            "..$([System.IO.Path]::DirectorySeparatorChar)",
            [System.StringComparison]::Ordinal)) {
        throw "O projeto precisa permanecer dentro do repositório: $repositoryRoot"
    }

    Assert-NoReparsePoint $repositoryRoot $projectRoot "O projeto"
    $profilePath = Resolve-InsideDirectory $projectRoot "rpa.editor.json" "O perfil"
    if (-not [System.IO.File]::Exists($profilePath)) {
        throw "Perfil rpa.editor.json não encontrado em '$projectRoot'."
    }
    $profile = [System.IO.File]::ReadAllText($profilePath, $utf8WithoutBom) |
        ConvertFrom-Json
    $projectFile = Resolve-InsideDirectory `
        $projectRoot ([string]$profile.projectFile) "O arquivo de projeto"
    $configurationFile = Resolve-InsideDirectory `
        $projectRoot ([string]$profile.configurationFile) "A configuração"
    Assert-LocalSecretPath $configurationFile "A configuração"
    return [pscustomobject]@{
        Root = $projectRoot
        Profile = $profilePath
        ProjectFile = $projectFile
        ConfigurationFile = $configurationFile
    }
}

function Initialize-ProjectConfiguration {
    $state = Resolve-ProjectState
    if (-not [System.IO.File]::Exists($state.ProjectFile)) {
        throw "Projeto .NET não encontrado em '$($state.ProjectFile)'."
    }
    if (-not [System.IO.File]::Exists($state.ConfigurationFile)) {
        $example = Join-Path $state.Root "appsettings.example.json"
        if (-not [System.IO.File]::Exists($example)) {
            throw "Configuração local ausente e exemplo não encontrado em '$example'."
        }
        Copy-Item -LiteralPath $example -Destination $state.ConfigurationFile
        Write-Host "Configuração local criada: $($state.ConfigurationFile)"
    }
    return $state
}

function Assert-CorePrerequisites {
    $missing = @()
    foreach ($name in @("dotnet", "pwsh", "git")) {
        if (-not (Test-Executable $name)) {
            $missing += $name
        }
    }
    if ($missing.Count -gt 0) {
        throw "Pré-requisitos ausentes: $($missing -join ', '). Consulte README.md."
    }
    if ($PSVersionTable.PSVersion -lt [System.Version]"7.2") {
        throw "PowerShell 7.2 ou posterior é obrigatório."
    }
}

function Assert-DockerAvailable {
    if (-not (Test-Executable "docker")) {
        throw "Docker não foi encontrado. Instale ou inicie o Docker Desktop."
    }
    $contextName = (& docker context show).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $contextName) {
        throw "Não foi possível identificar o contexto Docker."
    }
    $endpoint = (& docker context inspect $contextName `
        --format "{{.Endpoints.docker.Host}}").Trim()
    if ($LASTEXITCODE -ne 0 -or
        (-not $endpoint.StartsWith("npipe://", [System.StringComparison]::OrdinalIgnoreCase) -and
         -not $endpoint.StartsWith("unix://", [System.StringComparison]::OrdinalIgnoreCase))) {
        throw "O launcher recusa o contexto Docker remoto '$contextName' ($endpoint)."
    }
    Invoke-Checked "docker" @("info", "--format", "{{.ServerVersion}}") `
        "O daemon Docker local não está disponível"
}

function Get-OrCreateCaptchaApiKey {
    Assert-LocalSecretPath $captchaEnvironment "O arquivo de ambiente do solver"
    if ([System.IO.File]::Exists($captchaEnvironment)) {
        $line = [System.IO.File]::ReadAllLines($captchaEnvironment, $utf8WithoutBom) |
            Where-Object { $_.StartsWith("CAPTCHA_API_KEY=", [System.StringComparison]::Ordinal) } |
            Select-Object -First 1
        if ($line) {
            $value = $line.Substring("CAPTCHA_API_KEY=".Length).Trim()
            if ($value -match '^[A-F0-9]{64}$') {
                return $value
            }
        }
        throw "CAPTCHA_API_KEY precisa ser hexadecimal com 64 caracteres em '$captchaEnvironment'."
    }

    $apiKey = [Convert]::ToHexString(
        [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    [System.IO.File]::WriteAllText(
        $captchaEnvironment,
        "CAPTCHA_API_KEY=$apiKey`n",
        $utf8WithoutBom)
    Write-Host "Chave local do solver criada em arquivo ignorado pelo Git."
    return $apiKey
}

function Set-JsonProperty(
    [object]$Owner,
    [string]$Name,
    [object]$Value) {
    $property = $Owner.PSObject.Properties[$Name]
    if ($null -eq $property) {
        $Owner | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
    else {
        $property.Value = $Value
    }
}

function Set-ProjectCaptchaService(
    [pscustomobject]$State,
    [string]$ApiKey) {
    $configuration = [System.IO.File]::ReadAllText(
        $State.ConfigurationFile,
        $utf8WithoutBom) | ConvertFrom-Json
    $runtimeProperty = $configuration.PSObject.Properties["Runtime"]
    if ($null -eq $runtimeProperty -or $null -eq $runtimeProperty.Value) {
        throw "A configuração não contém a seção Runtime."
    }
    $runtime = $runtimeProperty.Value
    $captchaProperty = $runtime.PSObject.Properties["Captcha"]
    if ($null -eq $captchaProperty -or $null -eq $captchaProperty.Value) {
        Set-JsonProperty $runtime "Captcha" ([pscustomobject]@{})
    }
    $captcha = $runtime.PSObject.Properties["Captcha"].Value
    Set-JsonProperty $captcha "ServiceUrl" "http://127.0.0.1:8855"
    Set-JsonProperty $captcha "ServiceApiKey" $ApiKey
    $json = $configuration | ConvertTo-Json -Depth 100
    $temporaryPath = "$($State.ConfigurationFile).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            $json.ReplaceLineEndings("`n") + "`n",
            $utf8WithoutBom)
        [System.IO.File]::Move($temporaryPath, $State.ConfigurationFile, $true)
    }
    finally {
        [System.IO.File]::Delete($temporaryPath)
    }
    Write-Host "Projeto apontado para o solver local: $($State.ConfigurationFile)"
}

function Invoke-CaptchaCompose(
    [string[]]$Arguments,
    [string]$ApiKey = "unused-for-compose-management") {
    $composeArguments = @(
        "compose",
        "--project-name", $captchaComposeProject)
    $composeArguments += @("-f", $captchaCompose)
    $composeArguments += $Arguments
    $hadApiKey = Test-Path Env:CAPTCHA_API_KEY
    $previousApiKey = if ($hadApiKey) { $env:CAPTCHA_API_KEY } else { $null }
    $env:CAPTCHA_API_KEY = $ApiKey
    try {
        Invoke-Checked "docker" $composeArguments "O comando do captcha-solver falhou"
    }
    finally {
        if ($hadApiKey) {
            $env:CAPTCHA_API_KEY = $previousApiKey
        }
        else {
            Remove-Item Env:CAPTCHA_API_KEY
        }
    }
}

function Start-CaptchaSolver {
    Assert-DockerAvailable
    $state = Initialize-ProjectConfiguration
    $apiKey = Get-OrCreateCaptchaApiKey
    Write-Section "Iniciando captcha-solver"
    Invoke-CaptchaCompose `
        @("up", "-d", "--build", "--wait", "--wait-timeout", "300") $apiKey
    try {
        Set-ProjectCaptchaService $state $apiKey
    }
    catch {
        $configurationFailure = $_
        try {
            Invoke-CaptchaCompose @("down", "--remove-orphans") $apiKey
        }
        catch {
            Write-Warning "O solver iniciou, mas não pôde ser encerrado após a falha de configuração."
        }
        throw $configurationFailure
    }
    Write-Host "Solver pronto em http://127.0.0.1:8855"
}

function Stop-CaptchaSolver([switch]$RemoveImages) {
    Assert-DockerAvailable
    $arguments = @("down", "--remove-orphans")
    if ($RemoveImages) {
        $arguments += @("--rmi", "local", "--volumes")
    }
    Invoke-CaptchaCompose $arguments
}

function Start-CloudflareSidecar {
    Assert-DockerAvailable
    Write-Section "Iniciando sidecar $SidecarProvider"
    Invoke-Checked "docker" @(
        "compose",
        "--project-name", $sidecarComposeProject,
        "-f", $sidecarCompose,
        "--profile", $SidecarProvider,
        "up", "-d", $SidecarProvider) "O sidecar Cloudflare não iniciou"
    $sidecarPort = if ($env:CLOUDFLARE_SIDECAR_PORT) {
        $env:CLOUDFLARE_SIDECAR_PORT
    }
    else {
        "8191"
    }
    Write-Host "Container iniciado em http://127.0.0.1:$sidecarPort; valide o provider antes do uso."
}

function Stop-CloudflareSidecars([switch]$RemoveImages) {
    Assert-DockerAvailable
    $arguments = @(
        "compose",
        "--project-name", $sidecarComposeProject,
        "-f", $sidecarCompose,
        "--profile", "byparr",
        "--profile", "flaresolverr",
        "down", "--remove-orphans")
    if ($RemoveImages) {
        $arguments += "--volumes"
    }
    Invoke-Checked "docker" $arguments "Não foi possível parar os sidecars"
}

function Invoke-Doctor {
    Write-Section "Diagnóstico"
    $failed = $false
    foreach ($name in @("dotnet", "pwsh", "git", "node", "npm", "docker")) {
        $available = Test-Executable $name
        $color = if ($available) { "Green" } else { "Yellow" }
        Write-Host ("{0,-10} {1}" -f $name, $(if ($available) { "OK" } else { "ausente" })) `
            -ForegroundColor $color
        if ($name -in @("dotnet", "pwsh", "git") -and -not $available) {
            $failed = $true
        }
    }
    if (Test-Executable "dotnet") {
        Invoke-Checked "dotnet" @("--version") "O SDK solicitado por global.json não foi resolvido"
    }
    if ($PSVersionTable.PSVersion -lt [System.Version]"7.2") {
        Write-Host "PowerShell 7.2 ou posterior é obrigatório." -ForegroundColor Red
        $failed = $true
    }
    $state = Resolve-ProjectState
    Write-Host "Projeto: $($state.Root)"
    if (-not [System.IO.File]::Exists($state.ProjectFile)) {
        Write-Host "Projeto .NET: ausente" -ForegroundColor Red
        $failed = $true
    }
    else {
        Write-Host "Projeto .NET: OK"
    }
    if ([System.IO.File]::Exists($state.ConfigurationFile)) {
        try {
            $null = [System.IO.File]::ReadAllText(
                $state.ConfigurationFile,
                $utf8WithoutBom) | ConvertFrom-Json
            Write-Host "Configuração local: OK"
        }
        catch {
            Write-Host "Configuração local: JSON inválido" -ForegroundColor Red
            $failed = $true
        }
    }
    else {
        Write-Host "Configuração local: será criada no setup"
    }
    if ($failed) {
        throw "O diagnóstico encontrou pré-requisitos obrigatórios ausentes."
    }
}

function Install-Project {
    Assert-CorePrerequisites
    $state = Initialize-ProjectConfiguration
    if (-not $SkipBuild) {
        Write-Section "Restaurando e compilando"
        Invoke-Checked "dotnet" @("restore", $solutionPath) "O restore da solução falhou"
        Invoke-Checked "dotnet" @("build", $solutionPath, "-c", "Release", "--no-restore") `
            "O build da solução falhou"
    }
    if (-not $SkipBrowserInstall) {
        if (-not [System.IO.File]::Exists($playwrightInstaller)) {
            throw "Instalador do Playwright não encontrado; execute o setup sem -SkipBuild."
        }
        Write-Section "Instalando Chromium do Playwright"
        Invoke-Checked "pwsh" @("-NoProfile", "-File", $playwrightInstaller, "install", "chromium") `
            "A instalação do Chromium falhou"
    }
    switch ($CaptchaMode) {
        "Models" {
            Write-Section "Provisionando modelos locais"
            Invoke-Checked "pwsh" @(
                "-NoProfile",
                "-File", (Join-Path $repositoryRoot "tools\Get-CaptchaModels.ps1")) `
                "O provisionamento do OCR falhou"
            Invoke-Checked "pwsh" @(
                "-NoProfile",
                "-File", (Join-Path $repositoryRoot "tools\Get-CaptchaModels.ps1"),
                "-Suite", "hcaptcha") "O provisionamento do hCaptcha falhou"
        }
        "Docker" {
            Start-CaptchaSolver
        }
    }
    Write-Host ""
    Write-Host "Setup concluído para $($state.Root)" -ForegroundColor Green
}

function Open-Editor {
    param(
        [ValidateSet("None", "Models", "Docker")]
        [string]$RequestedCaptchaMode = $CaptchaMode
    )
    Assert-CorePrerequisites
    $state = Initialize-ProjectConfiguration
    if ($RequestedCaptchaMode -eq "Docker") {
        Start-CaptchaSolver
    }
    elseif ($RequestedCaptchaMode -eq "Models") {
        Invoke-Checked "pwsh" @(
            "-NoProfile",
            "-File", (Join-Path $repositoryRoot "tools\Get-CaptchaModels.ps1")) `
            "O provisionamento do OCR falhou"
    }
    if (-not $SkipBuild) {
        Write-Section "Compilando editor"
        Invoke-Checked "dotnet" @("build", $editorProject, "-c", "Release") `
            "O build do editor falhou"
    }
    if (-not [System.IO.File]::Exists($editorDll)) {
        throw "Editor compilado não encontrado; execute '.\rpablockly.cmd setup'."
    }
    $arguments = @($editorDll, "--project-root", $state.Root, "--url", $Url)
    if ($NoOpen) {
        $arguments += "--no-open"
    }
    Write-Section "Editor Blockly"
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "O editor encerrou com exit code $LASTEXITCODE."
    }
}

function Test-RpaProject {
    Assert-CorePrerequisites
    $state = Initialize-ProjectConfiguration
    $arguments = @(
        "run",
        "--project", $state.ProjectFile,
        "--configuration", "Release")
    if ($SkipBuild) {
        $arguments += "--no-build"
    }
    $arguments += @(
        "--",
        "--config", $state.ConfigurationFile,
        "--validate-only")
    Write-Section "Validando RPA"
    Invoke-Checked "dotnet" $arguments "A validação do RPA falhou"
}

function Show-Status {
    $state = Resolve-ProjectState
    Write-Section "Status local"
    Write-Host "Projeto: $($state.Root)"
    Write-Host "Configuração: $(if ([System.IO.File]::Exists($state.ConfigurationFile)) { 'presente' } else { 'ausente' })"
    Write-Host "Editor compilado: $(if ([System.IO.File]::Exists($editorDll)) { 'sim' } else { 'não' })"
    $modelRoot = Join-Path $repositoryRoot "captcha-models"
    $models = if ([System.IO.Directory]::Exists($modelRoot)) {
        [System.IO.Directory]::GetFiles($modelRoot, "*.onnx", "AllDirectories").Count
    }
    else {
        0
    }
    Write-Host "Modelos ONNX locais: $models"
    if (Test-Executable "docker") {
        try {
            Assert-DockerAvailable
            Write-Host ""
            Write-Host "Captcha-solver:"
            Invoke-CaptchaCompose @("ps")
            Write-Host "Sidecars:"
            Invoke-Checked "docker" @(
                "compose",
                "--project-name", $sidecarComposeProject,
                "-f", $sidecarCompose,
                "--profile", "byparr",
                "--profile", "flaresolverr",
                "ps") "Não foi possível consultar os sidecars"
        }
        catch {
            Write-Warning $_.Exception.Message
        }
    }
}

function Stop-AuxiliaryServices([switch]$RemoveImages) {
    $failures = @()
    try {
        Stop-CaptchaSolver -RemoveImages:$RemoveImages
    }
    catch {
        $failures += "captcha-solver: $($_.Exception.Message)"
    }
    try {
        Stop-CloudflareSidecars -RemoveImages:$RemoveImages
    }
    catch {
        $failures += "sidecars: $($_.Exception.Message)"
    }
    if ($failures.Count -gt 0) {
        throw "A limpeza terminou com falhas: $($failures -join ' | ')"
    }
}

function Clear-RpaBlocklyDocker {
    if (-not $Force) {
        $answer = Read-Host "Remover containers, imagens e volumes Docker do RpaBlockly? [s/N]"
        if ($answer -notin @("s", "S", "sim", "SIM")) {
            Write-Host "Limpeza cancelada."
            return
        }
    }
    Assert-DockerAvailable
    Stop-AuxiliaryServices -RemoveImages
    Write-Host "Recursos Docker do RpaBlockly removidos." -ForegroundColor Green
}

function Invoke-Menu {
    while ($true) {
        Write-Section "RpaBlockly"
        Write-Host "1. Preparar ambiente básico"
        Write-Host "2. Abrir editor"
        Write-Host "3. Abrir editor com solver Docker"
        Write-Host "4. Validar RPA"
        Write-Host "5. Diagnóstico e status"
        Write-Host "6. Parar serviços auxiliares"
        Write-Host "7. Limpar Docker do RpaBlockly"
        Write-Host "0. Sair"
        $choice = Read-Host "Escolha"
        try {
            switch ($choice) {
                "1" { Install-Project }
                "2" { Open-Editor }
                "3" { Open-Editor -RequestedCaptchaMode Docker }
                "4" { Test-RpaProject }
                "5" {
                    Invoke-Doctor
                    Show-Status
                }
                "6" { Stop-AuxiliaryServices }
                "7" { Clear-RpaBlocklyDocker }
                "0" { return }
                default { Write-Warning "Opção inválida." }
            }
        }
        catch {
            Write-Warning $_.Exception.Message
        }
    }
}

Push-Location $repositoryRoot
try {
    switch ($Command) {
        "menu" { Invoke-Menu }
        "doctor" { Invoke-Doctor }
        "setup" { Install-Project }
        "editor" { Open-Editor }
        "validate" { Test-RpaProject }
        "captcha-up" { Start-CaptchaSolver }
        "captcha-down" { Stop-CaptchaSolver }
        "sidecar-up" { Start-CloudflareSidecar }
        "sidecar-down" { Stop-CloudflareSidecars }
        "status" { Show-Status }
        "clean-docker" { Clear-RpaBlocklyDocker }
    }
}
finally {
    Pop-Location
}
