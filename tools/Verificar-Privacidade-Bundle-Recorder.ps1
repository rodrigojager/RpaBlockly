param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ [System.IO.File]::Exists($_) })]
    [string]$Bundle
)

$ErrorActionPreference = "Stop"
$bundlePath = (Resolve-Path -LiteralPath $Bundle).Path
if (-not $bundlePath.EndsWith(
    ".rpablockly.zip",
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "O arquivo deve terminar em .rpablockly.zip."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$requiredEntries = @(
    "manifest.json",
    "integrity.json",
    "package/flow.production.json",
    "package/locators.production.json",
    "package/rpa.policy.json",
    "samples/inputs.sample.json",
    "recording/session.json",
    "recording/events.json"
)
$forbiddenTerms = @(
    "fakepath",
    "document.cookie",
    '"cookie"',
    '"cookies"',
    "cookie=",
    "localStorage",
    "sessionStorage",
    "storageState"
)
$textExtensions = @(".json", ".jsonl", ".txt", ".md")
$textContentByPath = @{}
$archive = [System.IO.Compression.ZipFile]::OpenRead($bundlePath)
try {
    $entryNames = @($archive.Entries | ForEach-Object FullName)
    foreach ($required in $requiredEntries) {
        if ($required -notin $entryNames) {
            throw "Entrada obrigatória ausente no bundle: $required."
        }
    }

    foreach ($entry in $archive.Entries) {
        if ($entry.FullName.IndexOf("..", [System.StringComparison]::Ordinal) -ge 0 -or
            $entry.FullName.StartsWith("/", [System.StringComparison]::Ordinal) -or
            $entry.FullName.IndexOf("\", [System.StringComparison]::Ordinal) -ge 0) {
            throw "Caminho inseguro no bundle: $($entry.FullName)."
        }

        if ([System.IO.Path]::GetExtension($entry.FullName) -notin $textExtensions) {
            continue
        }
        if ($entry.Length -gt 25MB) {
            throw "Entrada textual acima do limite de inspeção: $($entry.FullName)."
        }

        $stream = $entry.Open()
        try {
            $reader = [System.IO.StreamReader]::new($stream, $strictUtf8, $false)
            try {
                $content = $reader.ReadToEnd()
            } finally {
                $reader.Dispose()
            }
        } finally {
            $stream.Dispose()
        }

        foreach ($term in $forbiddenTerms) {
            if ($content.IndexOf($term, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "O termo proibido '$term' foi encontrado em $($entry.FullName)."
            }
        }
        $textContentByPath[$entry.FullName] = $content
    }

    $manifest = $textContentByPath["manifest.json"] | ConvertFrom-Json
    $session = $textContentByPath["recording/session.json"] | ConvertFrom-Json
    if ($manifest.hasSecrets -ne $false -or
        $session.options.captureSecrets -ne $false -or
        @($entryNames | Where-Object {
            $_.StartsWith("secrets/", [System.StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0) {
        throw "O bundle de aceite contém ou declara captura de segredos."
    }
    if ($session.options.includeUploads -ne $false -or
        @($entryNames | Where-Object {
            $_.StartsWith("samples/uploads/", [System.StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0) {
        throw "O bundle de aceite contém ou declara bytes de upload."
    }
} finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "OK: estrutura mínima presente e nenhum caminho/termo proibido foi encontrado."
Write-Output "SHA-256: $hash"
