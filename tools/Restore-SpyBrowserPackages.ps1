[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot ".."),
    [string]$LockPath = (Join-Path $PSScriptRoot "spybrowser-packages.lock.json"),
    [string]$FeedPath,
    [string]$FixtureBaseUri
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$lockFile = [IO.Path]::GetFullPath($LockPath)
$feed = if ($FeedPath) { [IO.Path]::GetFullPath($FeedPath) } else { Join-Path $root "artifacts/spybrowser-feed" }
$expectedRelease = "https://github.com/rodrigojager/spybrowser/releases/download/v0.2.0-beta.2.1"
$expectedVersion = "0.2.0-beta.2.1"
$expectedCommit = "7983680083dd66aea2ff39a753533d02d8de2491"
if (-not [IO.File]::Exists($lockFile)) { throw "SpyBrowser lock file not found: $lockFile" }
try { $lock = [IO.File]::ReadAllText($lockFile) | ConvertFrom-Json } catch { throw "Invalid SpyBrowser lock JSON: $($_.Exception.Message)" }
if ($lock.version -ne $expectedVersion -or $lock.releaseUrl -ne $expectedRelease -or $lock.sourceCommit -ne $expectedCommit) {
    throw "SpyBrowser lock metadata does not match the approved release/version/source commit."
}
if (@($lock.packages).Count -ne 3) { throw "SpyBrowser lock must contain exactly the three approved packages." }
$allowed = @("SpyBrowser.Core", "SpyBrowser.Cursory", "SpyBrowser.Playwright")
if (@($lock.packages | Select-Object -ExpandProperty id | Sort-Object -Unique).Count -ne 3 -or
    @($lock.packages | Where-Object { $_.id -notin $allowed }).Count -ne 0) {
    throw "SpyBrowser lock must contain each approved package exactly once."
}
$baseUri = $expectedRelease
if ($FixtureBaseUri) {
    $fixture = [Uri]$FixtureBaseUri
    if (-not $fixture.IsLoopback -or $fixture.Scheme -ne "http") { throw "Fixture downloader URI must be HTTP loopback only." }
    $baseUri = $FixtureBaseUri.TrimEnd('/')
}
[IO.Directory]::CreateDirectory($feed) | Out-Null
foreach ($package in $lock.packages) {
    $id = [string]$package.id
    $version = [string]$package.version
    $hash = [string]$package.sha256
    if ($id -notin $allowed -or $version -ne $expectedVersion -or $hash -notmatch '^[A-Fa-f0-9]{64}$') {
        throw "Missing/invalid audited SHA256 in $lockFile for '$id'. Replace PLACEHOLDER with the release asset's independently verified SHA256 before restore."
    }
}
foreach ($id in $allowed) {
    $package = @($lock.packages | Where-Object id -eq $id)[0]
    $name = "$id.$expectedVersion.nupkg"
    $destination = Join-Path $feed $name
    $expectedHash = $package.sha256.ToUpperInvariant()
    if ([IO.File]::Exists($destination)) {
        $cachedHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($cachedHash -eq $expectedHash) { Write-Host "Verified cached $name"; continue }
        [IO.File]::Delete($destination)
    }
    $uri = "$baseUri/$name"
    $downloaded = $false
    for ($attempt = 1; $attempt -le 3 -and -not $downloaded; $attempt++) {
        $temporary = Join-Path $feed (".$name.{0}.tmp" -f [Guid]::NewGuid().ToString('N'))
        try {
            Invoke-WebRequest -Uri $uri -OutFile $temporary -TimeoutSec 45 -MaximumRedirection 3
            $actualHash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
            if ($actualHash -ne $expectedHash) { throw "SHA256 mismatch for $name (expected $expectedHash, got $actualHash)." }
            [IO.File]::Move($temporary, $destination, $true)
            $downloaded = $true
            Write-Host "Downloaded and verified $name"
        }
        catch {
            if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
            if ($attempt -eq 3) { throw "Unable to obtain verified $name after 3 attempts: $($_.Exception.Message)" }
            Start-Sleep -Seconds $attempt
        }
    }
}
