using System.Text.RegularExpressions;

namespace RpaFlow.Contracts.Recorder;

public static partial class RecorderContractValidator
{
    public static void Validate(RecorderBundleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Require(manifest.BundleFormat == "rpablockly-recorder", "bundleFormat inválido.");
        Require(manifest.BundleVersion == 1, "bundleVersion deve ser 1.");
        Require(IdPattern().IsMatch(manifest.BundleId), "bundleId inválido.");
        Require(manifest.CreatedAtUtc != default, "createdAtUtc é obrigatório.");
        RequireText(manifest.RecorderVersion, 64, "recorderVersion");
        RequireText(manifest.GeneratorVersion, 64, "generatorVersion");
        Require(manifest.RpaPackageRoot == "package", "rpaPackageRoot deve ser package.");
        Require(manifest.Origin == "chrome-recorder", "origin inválida.");
        RequireText(manifest.DisplayName, 200, "displayName");
        Require(!manifest.ContainsReplay, "Bundles Recorder não podem conter replay.");
        Require(manifest.Schemas is
            { Flow: 2, Locators: 1, Policy: 1, Session: 1, Evidence: 1, Issues: 1, Integrity: 1 },
            "Versões de schema incompatíveis.");
        Require(manifest.Files.Count is > 0 and <= RecorderBundleLimits.MaximumEntries,
            "Quantidade de arquivos fora do limite.");
        ValidateUniquePaths(manifest.Files);
        Require(manifest.StepCount is >= 0 and <= 10_000, "stepCount fora do limite.");
        Require(manifest.BlockingIssueCount is >= 0 and <= 10_000,
            "blockingIssueCount fora do limite.");
        Require(manifest.WarningIssueCount is >= 0 and <= 10_000,
            "warningIssueCount fora do limite.");
        Require(!manifest.HasSecrets || !string.IsNullOrWhiteSpace(manifest.RecipientKeyId),
            "Bundle com segredos exige recipientKeyId.");
    }

    public static void Validate(RecorderSessionDocument session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Require(session.SchemaVersion == 1, "schemaVersion da sessão deve ser 1.");
        Require(IdPattern().IsMatch(session.SessionId), "sessionId inválido.");
        RequireText(session.Name, 200, "name");
        Require(session.StartedAtUtc != default, "startedAtUtc é obrigatório.");
        if (session.CompletedAtUtc is { } completed)
        {
            Require(completed >= session.StartedAtUtc, "completedAtUtc antecede o início.");
            Require(completed - session.StartedAtUtc <=
                TimeSpan.FromMinutes(RecorderBundleLimits.MaximumSessionDurationMinutes),
                "Sessão excedeu a duração máxima.");
        }
        Require(session.EventCount is >= 0 and <= RecorderBundleLimits.MaximumSessionEvents,
            "eventCount fora do limite.");
        Require(session.Associations.Count <= 10_000, "Associações excederam o limite.");
        RequireUnique(session.Tabs.Select(item => item.Id), "tab");
        RequireUnique(session.Frames.Select(item => item.Id), "frame");
        RequireUnique(session.Associations.Select(item => item.EventId), "associação de evento");
        foreach (var origin in session.Origins)
        {
            Require(Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https", $"Origem inválida: {origin}.");
        }
    }

    public static void Validate(RecorderEvidenceDocument evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Require(evidence.SchemaVersion == 1, "schemaVersion de evidência deve ser 1.");
        Require(evidence.Items.Count <= RecorderBundleLimits.MaximumEvidenceItems,
            "Quantidade de evidências excedeu o limite.");
        RequireUnique(evidence.Items.Select(item => item.Id), "evidência");
        foreach (var item in evidence.Items)
        {
            RequireText(item.Id, 128, "evidence.id");
            Require(item.MimeType == "image/webp", "Evidência deve usar WebP.");
            Require(item.Width is >= 1 and <= 4096 && item.Height is >= 1 and <= 4096,
                "Dimensões da evidência fora do limite.");
            Require(item.ByteLength is >= 1 and <= RecorderBundleLimits.MaximumEvidenceBytes,
                "Tamanho da evidência fora do limite.");
            ValidateRelativePath(item.Path);
            ValidateRelativePath(item.ThumbnailPath);
            Require(item.Path.StartsWith("evidence/", StringComparison.Ordinal) &&
                item.Path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase),
                "Caminho de evidência inválido.");
            Require(item.ThumbnailPath.StartsWith("evidence/thumbnails/", StringComparison.Ordinal),
                "Caminho de thumbnail inválido.");
            foreach (var mask in item.Masks)
            {
                Require(mask.X >= 0 && mask.Y >= 0 && mask.Width > 0 && mask.Height > 0,
                    "Máscara de evidência inválida.");
            }
        }
    }

    public static void Validate(RecorderIssuesDocument issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Require(issues.SchemaVersion == 1, "schemaVersion de issues deve ser 1.");
        Require(issues.Issues.Count <= 10_000, "Quantidade de issues excedeu o limite.");
        RequireUnique(issues.Issues.Select(item => item.Id), "issue");
        foreach (var issue in issues.Issues)
        {
            RequireText(issue.Id, 128, "issue.id");
            RequireText(issue.Title, 300, "issue.title");
            Require(issue.TechnicalDetail.Length <= RecorderBundleLimits.MaximumTextLength,
                "Detalhe técnico excedeu o limite.");
            Require(issue.EvidenceIds.Count <= 20, "Issue referencia evidências demais.");
            Require(issue.ResolutionOptions.Count <= 10, "Issue possui opções demais.");
        }
    }

    public static void Validate(RecorderIntegrityDocument integrity)
    {
        ArgumentNullException.ThrowIfNull(integrity);
        Require(integrity.SchemaVersion == 1, "schemaVersion de integridade deve ser 1.");
        Require(integrity.Entries.Count is > 0 and <= RecorderBundleLimits.MaximumEntries,
            "Quantidade de entradas de integridade fora do limite.");
        ValidateUniquePaths(integrity.Entries.Select(item => item.Path));
        foreach (var entry in integrity.Entries)
        {
            Require(Sha256Pattern().IsMatch(entry.Sha256),
                $"SHA-256 inválido para {entry.Path}.");
            Require(entry.Size is >= 0 and <= RecorderBundleLimits.MaximumUncompressedEntryBytes,
                $"Tamanho inválido para {entry.Path}.");
        }
    }

    public static void Validate(
        RecorderSecretsIndexDocument index,
        IReadOnlyList<RecorderEncryptedSecretEnvelope> envelopes,
        string recipientKeyId)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(envelopes);
        Require(index.SchemaVersion == 1, "schemaVersion de secrets deve ser 1.");
        Require(index.Algorithm == "AES-256-GCM+RSA-OAEP-SHA-256",
            "Algoritmo de secrets inválido.");
        RequireUnique(index.Items, "referência de segredo");
        RequireUnique(envelopes.Select(item => item.Reference), "envelope de segredo");
        Require(index.Items.ToHashSet(StringComparer.Ordinal).SetEquals(
            envelopes.Select(item => item.Reference)),
            "secrets/index.json diverge dos envelopes.");
        foreach (var envelope in envelopes)
        {
            Require(envelope.SchemaVersion == 1, "schemaVersion do envelope deve ser 1.");
            Require(SecretReferencePattern().IsMatch(envelope.Reference),
                "Referência de segredo inválida.");
            Require(envelope.KeyId == recipientKeyId,
                "keyId do envelope diverge do manifest.");
            Require(envelope.Algorithm == index.Algorithm,
                "Algoritmo do envelope diverge do índice.");
            RequireBase64(envelope.Iv, 12, 12, "iv");
            RequireBase64(envelope.Aad, 1, 2_000, "aad");
            RequireBase64(envelope.Ciphertext, 17, 10_000, "ciphertext");
            RequireBase64(envelope.WrappedKey, 256, 1_024, "wrappedKey");
        }
    }

    public static void Validate(RecorderUploadsDocument uploads)
    {
        ArgumentNullException.ThrowIfNull(uploads);
        Require(uploads.SchemaVersion == 1, "schemaVersion de uploads deve ser 1.");
        RequireUnique(uploads.Items.Select(item => item.Name), "nome de upload");
        long total = 0;
        foreach (var item in uploads.Items)
        {
            Require(FileNamePattern().IsMatch(item.Name), "Nome sanitizado de upload inválido.");
            Require(!DangerousUploadExtensionPattern().IsMatch(item.Name),
                "Extensão de upload bloqueada.");
            RequireText(item.MimeType, 200, "upload.mimeType");
            Require(!item.MimeType.Contains('\r') && !item.MimeType.Contains('\n'),
                "MIME de upload inválido.");
            Require(item.Size is >= 0 and <= RecorderBundleLimits.MaximumUploadBytes,
                "Upload excede o limite individual.");
            Require(Sha256Pattern().IsMatch(item.Sha256), "Hash de upload inválido.");
            if (item.Included) total = checked(total + item.Size);
        }
        Require(total <= RecorderBundleLimits.MaximumTotalUploadBytes,
            "Uploads excedem o limite total.");
    }

    public static void ValidateRelativePath(string path)
    {
        RequireText(path, 240, "path");
        Require(!Path.IsPathRooted(path) && !path.Contains('\\') &&
            !path.Split('/').Any(segment => segment is "" or "." or "..") &&
            !path.Contains(':', StringComparison.Ordinal),
            $"Caminho inseguro no bundle: {path}.");
    }

    private static void ValidateUniquePaths(IEnumerable<string> paths)
    {
        var materialized = paths.ToArray();
        foreach (var path in materialized) ValidateRelativePath(path);
        Require(materialized.Distinct(StringComparer.OrdinalIgnoreCase).Count() == materialized.Length,
            "O bundle contém caminhos duplicados sem diferença de caixa.");
    }

    private static void RequireUnique(IEnumerable<string> ids, string kind)
    {
        var materialized = ids.ToArray();
        Require(materialized.All(value => !string.IsNullOrWhiteSpace(value)) &&
            materialized.Distinct(StringComparer.OrdinalIgnoreCase).Count() == materialized.Length,
            $"ID de {kind} vazio ou duplicado.");
    }

    private static void RequireText(string? value, int maximumLength, string path) =>
        Require(!string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength,
            $"{path} é obrigatório e deve ter até {maximumLength} caracteres.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireBase64(
        string value,
        int minimumBytes,
        int maximumBytes,
        string path)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            Require(bytes.Length >= minimumBytes && bytes.Length <= maximumBytes,
                $"{path} possui tamanho inválido.");
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{path} não contém Base64 válido.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^secret\\.recorded\\.[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretReferencePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,179}$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    [GeneratedRegex("\\.(?:exe|dll|com|bat|cmd|ps1|js|mjs|html?|scr|msi)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DangerousUploadExtensionPattern();
}
