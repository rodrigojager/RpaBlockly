using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RpaFlow.Contracts.Recorder;
using RpaFlow.Contracts.V2;
using RpaFlow.Packages;

namespace RpaFlow.Editor.Recorder;

internal sealed partial class RecorderBundleInspector
{
    public const long MaximumArchiveBytes = 50 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly IReadOnlySet<string> FixedPaths = new HashSet<string>(
        [
            "manifest.json",
            "integrity.json",
            "package/flow.production.json",
            "package/locators.production.json",
            "package/rpa.policy.json",
            "samples/inputs.sample.json",
            "recording/session.json",
            "recording/events.json",
            "recording/issues.json",
            "recording/comments.json",
            "recording/uploads.json",
            "evidence/index.json",
            "secrets/index.json"
        ],
        StringComparer.Ordinal);

    public Task<InspectedRecorderBundle> InspectAsync(
        byte[] archiveBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archiveBytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (archiveBytes.Length == 0 || archiveBytes.LongLength > MaximumArchiveBytes)
        {
            throw new InvalidOperationException(
                $"O bundle deve possuir entre 1 byte e {MaximumArchiveBytes} bytes compactados.");
        }

        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var archive = OpenArchive(stream);
        var entries = InspectEntries(archive);
        var bytes = ReadAllEntries(entries, cancellationToken);
        var integrity = Deserialize<RecorderIntegrityDocument>(
            Require(bytes, "integrity.json"),
            "integrity.json");
        RecorderContractValidator.Validate(integrity);
        ValidateIntegrity(bytes, integrity);

        var manifest = Deserialize<RecorderBundleManifest>(
            Require(bytes, "manifest.json"),
            "manifest.json");
        RecorderContractValidator.Validate(manifest);
        ValidateManifestFileList(manifest, bytes.Keys);

        var session = Deserialize<RecorderSessionDocument>(
            Require(bytes, "recording/session.json"),
            "recording/session.json");
        var issues = Deserialize<RecorderIssuesDocument>(
            Require(bytes, "recording/issues.json"),
            "recording/issues.json");
        var evidence = bytes.TryGetValue("evidence/index.json", out var evidenceBytes)
            ? Deserialize<RecorderEvidenceDocument>(evidenceBytes, "evidence/index.json")
            : new RecorderEvidenceDocument();
        RecorderContractValidator.Validate(session);
        RecorderContractValidator.Validate(issues);
        RecorderContractValidator.Validate(evidence);

        var package = new RpaPackageDocuments(
            Deserialize<FlowDefinition>(Require(bytes, "package/flow.production.json"),
                "package/flow.production.json"),
            Deserialize<LocatorCatalog>(Require(bytes, "package/locators.production.json"),
                "package/locators.production.json"),
            Deserialize<RpaPolicyDefinition>(Require(bytes, "package/rpa.policy.json"),
                "package/rpa.policy.json"));
        RpaPackageValidator.Validate(package);

        var events = ParseLimitedDocument(
            Require(bytes, "recording/events.json"),
            "recording/events.json");
        var comments = bytes.TryGetValue("recording/comments.json", out var commentsBytes)
            ? ParseLimitedDocument(commentsBytes, "recording/comments.json")
            : EmptyArrayDocument("comments");
        var samples = bytes.TryGetValue("samples/inputs.sample.json", out var samplesBytes)
            ? ParseLimitedDocument(samplesBytes, "samples/inputs.sample.json")
            : EmptyObjectDocument();
        ValidateCrossDocumentConsistency(
            manifest,
            session,
            evidence,
            issues,
            package,
            events,
            comments,
            bytes);
        ValidateSecrets(manifest, package, bytes);
        ValidateUploads(manifest, bytes);

        return Task.FromResult(new InspectedRecorderBundle(
            manifest,
            session,
            evidence,
            issues,
            integrity,
            package,
            events,
            comments,
            samples,
            archiveBytes.ToArray(),
            bytes));
    }

    private static ZipArchive OpenArchive(Stream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException("O arquivo não é um ZIP Recorder válido.", exception);
        }
    }

    private static IReadOnlyList<ZipArchiveEntry> InspectEntries(ZipArchive archive)
    {
        if (archive.Entries.Count is 0 or > RecorderBundleLimits.MaximumEntries)
        {
            throw new InvalidOperationException("Quantidade de entradas do ZIP fora do limite.");
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateEntryPath(entry.FullName);
            if (!paths.Add(entry.FullName))
            {
                throw new InvalidOperationException(
                    "O ZIP contém caminhos duplicados sem diferença de caixa.");
            }
            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Diretórios explícitos não são aceitos no bundle.");
            }
            if (IsSymbolicLink(entry))
            {
                throw new InvalidOperationException($"Link simbólico rejeitado: {entry.FullName}.");
            }
            if (!IsAllowedPath(entry.FullName))
            {
                throw new InvalidOperationException($"Tipo de entrada inesperado: {entry.FullName}.");
            }
            if (entry.CompressedLength > RecorderBundleLimits.MaximumCompressedEntryBytes ||
                entry.Length > RecorderBundleLimits.MaximumUncompressedEntryBytes)
            {
                throw new InvalidOperationException($"Entrada acima do limite: {entry.FullName}.");
            }
            var ratio = entry.CompressedLength == 0
                ? entry.Length == 0 ? 1 : double.PositiveInfinity
                : (double)entry.Length / entry.CompressedLength;
            if (ratio > RecorderBundleLimits.MaximumCompressionRatio)
            {
                throw new InvalidOperationException($"Razão de compressão insegura: {entry.FullName}.");
            }
            total = checked(total + entry.Length);
            if (total > RecorderBundleLimits.MaximumTotalUncompressedBytes)
            {
                throw new InvalidOperationException("O ZIP excede o limite total descompactado.");
            }
        }
        return archive.Entries.ToArray();
    }

    private static Dictionary<string, byte[]> ReadAllEntries(
        IReadOnlyList<ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in entries.OrderBy(item => item.FullName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = entry.Open();
            using var target = new MemoryStream(capacity: checked((int)entry.Length));
            source.CopyTo(target);
            if (target.Length != entry.Length)
            {
                throw new InvalidOperationException($"Tamanho divergente em {entry.FullName}.");
            }
            result.Add(entry.FullName, target.ToArray());
        }
        return result;
    }

    private static void ValidateIntegrity(
        IReadOnlyDictionary<string, byte[]> bytes,
        RecorderIntegrityDocument integrity)
    {
        var expected = bytes.Keys
            .Where(path => path != "integrity.json")
            .ToHashSet(StringComparer.Ordinal);
        var declared = integrity.Entries.Select(item => item.Path)
            .ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(declared))
        {
            throw new InvalidOperationException(
                "integrity.json não descreve exatamente as entradas do bundle.");
        }
        foreach (var item in integrity.Entries)
        {
            var content = Require(bytes, item.Path);
            var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            if (content.LongLength != item.Size ||
                !hash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Hash ou tamanho divergente em {item.Path}.");
            }
        }
    }

    private static void ValidateManifestFileList(
        RecorderBundleManifest manifest,
        IEnumerable<string> archivePaths)
    {
        var expected = archivePaths.Where(path => path is not "manifest.json" and not "integrity.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (!manifest.Files.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "manifest.files deve listar em ordem todas as entradas de conteúdo.");
        }
    }

    private static void ValidateCrossDocumentConsistency(
        RecorderBundleManifest manifest,
        RecorderSessionDocument session,
        RecorderEvidenceDocument evidence,
        RecorderIssuesDocument issues,
        RpaPackageDocuments package,
        JsonElement events,
        JsonElement comments,
        IReadOnlyDictionary<string, byte[]> bytes)
    {
        if (manifest.StepCount != package.Flow.Actions.Count)
        {
            throw new InvalidOperationException("stepCount diverge do fluxo V2.");
        }
        var unresolved = issues.Issues.Where(issue => !issue.Resolved).ToArray();
        if (manifest.BlockingIssueCount != unresolved.Count(issue =>
                issue.Severity == RecorderIssueSeverity.Blocking) ||
            manifest.WarningIssueCount != unresolved.Count(issue =>
                issue.Severity == RecorderIssueSeverity.Warning))
        {
            throw new InvalidOperationException("Contadores de issues divergem do manifest.");
        }
        ValidateEventDocument(events, session.EventCount);
        ValidateComments(comments);
        foreach (var item in evidence.Items)
        {
            if (!bytes.ContainsKey(item.Path) || !bytes.ContainsKey(item.ThumbnailPath) ||
                bytes[item.Path].LongLength != item.ByteLength)
            {
                throw new InvalidOperationException(
                    $"Arquivos da evidência '{item.Id}' estão ausentes ou divergentes.");
            }
        }
        var hasSecretEntries = bytes.Keys.Any(path => path.StartsWith("secrets/", StringComparison.Ordinal));
        var hasUploadEntries = bytes.ContainsKey("recording/uploads.json") ||
            bytes.Keys.Any(path =>
                path.StartsWith("samples/uploads/", StringComparison.Ordinal));
        if (manifest.HasSecrets != hasSecretEntries || manifest.HasUploads != hasUploadEntries)
        {
            throw new InvalidOperationException("Flags de segredos/uploads divergem do conteúdo.");
        }
    }

    private static void ValidateEventDocument(JsonElement document, int expectedCount)
    {
        if (document.ValueKind != JsonValueKind.Object ||
            !document.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1 ||
            !document.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array ||
            events.GetArrayLength() != expectedCount ||
            events.GetArrayLength() > RecorderBundleLimits.MaximumSessionEvents)
        {
            throw new InvalidOperationException("recording/events.json é inconsistente.");
        }
        foreach (var item in events.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString()) ||
                item.TryGetProperty("transientSecret", out _) || item.TryGetProperty("value", out _))
            {
                throw new InvalidOperationException(
                    "Evento inválido ou com valor que deveria estar em samples/segredos.");
            }
        }
    }

    private static void ValidateSecrets(
        RecorderBundleManifest manifest,
        RpaPackageDocuments package,
        IReadOnlyDictionary<string, byte[]> bytes)
    {
        if (!manifest.HasSecrets) return;
        var keyId = manifest.RecipientKeyId
            ?? throw new InvalidOperationException("Bundle com segredo exige recipientKeyId.");
        var index = Deserialize<RecorderSecretsIndexDocument>(
            Require(bytes, "secrets/index.json"),
            "secrets/index.json");
        var envelopes = bytes.Where(item =>
                item.Key.StartsWith("secrets/", StringComparison.Ordinal) &&
                item.Key != "secrets/index.json")
            .Select(item => Deserialize<RecorderEncryptedSecretEnvelope>(
                item.Value,
                item.Key))
            .ToArray();
        RecorderContractValidator.Validate(index, envelopes, keyId);
        var references = RecorderPackageMerger.SecretReferences(package)
            .ToHashSet(StringComparer.Ordinal);
        if (!references.SetEquals(envelopes.Select(item => item.Reference)))
        {
            throw new InvalidOperationException(
                "Referências secret.recorded divergem dos envelopes cifrados.");
        }
    }

    private static void ValidateUploads(
        RecorderBundleManifest manifest,
        IReadOnlyDictionary<string, byte[]> bytes)
    {
        if (!manifest.HasUploads) return;
        var uploads = Deserialize<RecorderUploadsDocument>(
            Require(bytes, "recording/uploads.json"),
            "recording/uploads.json");
        RecorderContractValidator.Validate(uploads);
        var declaredContent = uploads.Items.Where(item => item.Included)
            .Select(item => "samples/uploads/" + item.Name)
            .ToHashSet(StringComparer.Ordinal);
        var actualContent = bytes.Keys.Where(path =>
                path.StartsWith("samples/uploads/", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        if (!declaredContent.SetEquals(actualContent))
        {
            throw new InvalidOperationException(
                "Conteúdo de uploads diverge do consentimento registrado.");
        }
        foreach (var item in uploads.Items.Where(item => item.Included))
        {
            var content = bytes["samples/uploads/" + item.Name];
            var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            if (content.LongLength != item.Size ||
                !hash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Upload '{item.Name}' diverge dos metadados.");
            }
        }
    }

    private static void ValidateComments(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object ||
            !document.TryGetProperty("comments", out var comments) ||
            comments.ValueKind != JsonValueKind.Array || comments.GetArrayLength() > 10_000)
        {
            throw new InvalidOperationException("recording/comments.json é inválido.");
        }
        foreach (var comment in comments.EnumerateArray())
        {
            if (!comment.TryGetProperty("text", out var text) ||
                text.GetString()?.Length > 1_000)
            {
                throw new InvalidOperationException("Comentário ausente ou acima do limite.");
            }
        }
    }

    private static JsonElement ParseLimitedDocument(byte[] bytes, string description)
    {
        if (bytes.LongLength > RecorderBundleLimits.MaximumUncompressedEntryBytes)
        {
            throw new InvalidOperationException($"{description} excede o limite.");
        }
        try
        {
            using var document = JsonDocument.Parse(StrictUtf8.GetString(bytes), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 128,
                AllowTrailingCommas = false
            });
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{description} não contém JSON válido.", exception);
        }
    }

    private static T Deserialize<T>(byte[] bytes, string description) where T : class
    {
        try
        {
            return V2JsonSerializer.Deserialize<T>(StrictUtf8.GetString(bytes), description);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"{description} não corresponde ao contrato estrito: {exception.Message}",
                exception);
        }
    }

    private static byte[] Require(IReadOnlyDictionary<string, byte[]> bytes, string path) =>
        bytes.TryGetValue(path, out var value)
            ? value
            : throw new InvalidOperationException($"Entrada obrigatória ausente: {path}.");

    private static void ValidateEntryPath(string path)
    {
        RecorderContractValidator.ValidateRelativePath(path);
        if (!PathPattern().IsMatch(path))
        {
            throw new InvalidOperationException($"Caminho com caractere enganoso: {path}.");
        }
    }

    private static bool IsAllowedPath(string path) =>
        FixedPaths.Contains(path) ||
        (path.StartsWith("evidence/", StringComparison.Ordinal) &&
         path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) ||
        (path.StartsWith("secrets/", StringComparison.Ordinal) &&
         path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
        (path.StartsWith("samples/uploads/", StringComparison.Ordinal) &&
         !DangerousUploadExtension().IsMatch(path));

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var unixMode = ((uint)entry.ExternalAttributes >> 16) & 0xF000u;
        return unixMode == 0xA000u;
    }

    private static JsonElement EmptyArrayDocument(string property) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["schemaVersion"] = 1,
            [property] = Array.Empty<object>()
        });

    private static JsonElement EmptyObjectDocument() =>
        JsonSerializer.SerializeToElement(new { input = new { } });

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]{0,239}$", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();

    [GeneratedRegex("\\.(?:exe|dll|com|bat|cmd|ps1|js|mjs|html?|scr|msi)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DangerousUploadExtension();
}
