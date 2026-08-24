using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RpaFlow.Contracts.Recorder;
using RpaFlow.Contracts.V2;
using RpaFlow.Editor.Configuration;
using RpaFlow.Packages;

namespace RpaFlow.Editor.Recorder;

internal sealed class RecorderImportService
{
    private readonly EditorPaths _paths;
    private readonly FileRpaPackageStore _store;
    private readonly RecorderStagingService _staging;
    private readonly RecorderPackageMerger _merger;
    private readonly RecorderEvidenceArchive _archive;
    private readonly RecorderImportAudit _audit;

    public RecorderImportService(
        EditorPaths paths,
        RecorderStagingService staging,
        RecorderPackageMerger merger,
        RecorderEvidenceArchive archive,
        RecorderImportAudit audit)
    {
        _paths = paths;
        _store = new FileRpaPackageStore(paths.PackageStoreRoot);
        _staging = staging;
        _merger = merger;
        _archive = archive;
        _audit = audit;
    }

    public async Task<RecorderInspectResult> InspectAsync(
        byte[] archiveBytes,
        CancellationToken cancellationToken)
    {
        var entry = await _staging.CreateAsync(archiveBytes, cancellationToken);
        var preview = await CreatePreviewAsync(entry.Bundle, cancellationToken);
        await TryAuditAsync(
            "inspect", entry.Id, entry.Bundle.Manifest.BundleId, "accepted", null,
            cancellationToken);
        return new RecorderInspectResult(entry.Id, entry.Token, entry.ExpiresAtUtc, preview);
    }

    public async Task<RecorderImportPreview> GetAsync(
        string stagingId,
        string stagingToken,
        CancellationToken cancellationToken)
    {
        var entry = await _staging.GetAsync(stagingId, stagingToken, cancellationToken);
        return await CreatePreviewAsync(entry.Bundle, cancellationToken);
    }

    public async Task<(byte[] Bytes, string FileName)> GetEvidenceAsync(
        string stagingId,
        string stagingToken,
        string evidenceId,
        bool thumbnail,
        CancellationToken cancellationToken)
    {
        var entry = await _staging.GetAsync(stagingId, stagingToken, cancellationToken);
        var evidence = entry.Bundle.Evidence.Items.FirstOrDefault(item =>
            item.Id.Equals(evidenceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Evidência não encontrada no staging.");
        var path = thumbnail ? evidence.ThumbnailPath : evidence.Path;
        return (entry.Bundle.EntryBytes[path].ToArray(), Path.GetFileName(path));
    }

    public async Task<RecorderImportValidationResult> ValidateAsync(
        string stagingId,
        string stagingToken,
        RecorderImportApplyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (entry, current, merged) = await BuildMergeAsync(
                stagingId, stagingToken, request, cancellationToken);
            return new RecorderImportValidationResult(
                true,
                request.ExpectedRevision,
                request.Mode,
                merged.Documents.Flow.Name,
                CountActions(merged.Documents.Flow),
                merged.Documents.Locators.Locators.Count,
                merged.IdRemappings,
                [],
                merged.Warnings.Concat(RpaPackageValidator.Validate(merged.Documents).Warnings).ToArray());
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                                 KeyNotFoundException or
                                                 JsonException)
        {
            return new RecorderImportValidationResult(
                false,
                request.ExpectedRevision,
                request.Mode,
                string.Empty,
                0,
                0,
                new Dictionary<string, string>(),
                [exception.Message],
                []);
        }
    }

    public async Task<RecorderImportApplyResult> ApplyAsync(
        string stagingId,
        string stagingToken,
        RecorderImportApplyRequest request,
        CancellationToken cancellationToken)
    {
        var requestHash = ComputeRequestHash(request);
        var entry = await _staging.GetAsync(stagingId, stagingToken, cancellationToken);
        if (entry.AppliedRequestHash == requestHash && entry.AppliedRevision is not null)
        {
            var currentAfterReplay = await _store.LoadAsync(
                _paths.RpaId,
                null,
                cancellationToken);
            if (!currentAfterReplay.Revision.Value.Equals(
                    entry.AppliedRevision,
                    StringComparison.Ordinal))
            {
                throw new PackageRevisionConflictException(
                    "O apply já foi concluído, mas o pacote recebeu outra revisão depois dele.");
            }
            var replay = await _store.LoadAsync(
                _paths.RpaId,
                new PackageRevision(entry.AppliedRevision),
                cancellationToken);
            return ToApplyResult(
                replay,
                entry.AppliedRemappings ?? new Dictionary<string, string>(),
                EvidenceRelativePath(replay.Revision.Value, entry.Bundle.Manifest.BundleId),
                true);
        }

        var (_, current, merged) = await BuildMergeAsync(
            stagingId, stagingToken, request, cancellationToken);
        var contentHash = CanonicalJson.ComputePackageHash(merged.Documents);
        PreparedArchive? prepared = null;
        PackageWriteResult? published = null;
        try
        {
            prepared = await _archive.PrepareAsync(
                contentHash,
                entry.Bundle,
                merged.IdRemappings,
                cancellationToken);
            published = await _store.PublishAsync(
                _paths.RpaId,
                merged.Documents,
                current.Revision,
                cancellationToken);
            await _staging.MarkAppliedAsync(
                stagingId,
                stagingToken,
                requestHash,
                published.Revision.Value,
                merged.IdRemappings,
                CancellationToken.None);
            var reopened = await _store.LoadAsync(
                _paths.RpaId,
                published.Revision,
                CancellationToken.None);
            if (!reopened.ContentHash.Equals(contentHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "O pacote reaberto diverge semanticamente do merge validado.");
            }
            await TryAuditAsync(
                "apply", stagingId, entry.Bundle.Manifest.BundleId, "published",
                published.Revision.Value, CancellationToken.None);
            return ToApplyResult(
                reopened,
                merged.IdRemappings,
                prepared.RelativeArchivePath,
                false,
                merged.Warnings);
        }
        catch
        {
            if (published is null && prepared is not null) _archive.Rollback(prepared);
            await TryAuditAsync(
                "apply",
                stagingId,
                entry.Bundle.Manifest.BundleId,
                published is null ? "rejected" : "published-with-response-error",
                published?.Revision.Value,
                CancellationToken.None);
            throw;
        }
    }

    public async Task DeleteAsync(
        string stagingId,
        string stagingToken,
        CancellationToken cancellationToken)
    {
        await _staging.DeleteAsync(stagingId, stagingToken, cancellationToken);
    }

    private async Task<(StagingEntry Entry, RpaPackageSnapshot Current, RecorderMergeResult Merged)>
        BuildMergeAsync(
            string stagingId,
            string stagingToken,
            RecorderImportApplyRequest request,
            CancellationToken cancellationToken)
    {
        var entry = await _staging.GetAsync(stagingId, stagingToken, cancellationToken);
        var current = await _store.LoadAsync(_paths.RpaId, null, cancellationToken);
        if (!current.Revision.Value.Equals(request.ExpectedRevision, StringComparison.Ordinal))
        {
            throw new PackageRevisionConflictException(
                $"A revisão esperada '{request.ExpectedRevision}' mudou para '{current.Revision.Value}'.");
        }
        var merged = _merger.Merge(current.CopyDocuments(), entry.Bundle, request);
        RpaPackageValidator.Validate(merged.Documents);
        return (entry, current, merged);
    }

    private async Task<RecorderImportPreview> CreatePreviewAsync(
        InspectedRecorderBundle bundle,
        CancellationToken cancellationToken)
    {
        var current = await _store.LoadAsync(_paths.RpaId, null, cancellationToken);
        var comments = ParseComments(bundle.Comments);
        var actions = EnumerateActions(bundle.Package.Flow)
            .ToDictionary(action => action.Id, StringComparer.OrdinalIgnoreCase);
        var evidenceById = bundle.Evidence.Items.ToDictionary(
            item => item.Id,
            StringComparer.OrdinalIgnoreCase);
        var timeline = bundle.Session.Associations.Select(association =>
        {
            actions.TryGetValue(association.ActionId, out var action);
            var comment = comments.GetValueOrDefault(association.ActionId);
            return new RecorderTimelineItem(
                association.EventId,
                association.ActionId,
                action?.Type ?? "unknown",
                action?.Name ?? association.ActionId,
                association.LocatorId,
                association.EvidenceId,
                comment);
        }).ToArray();
        return new RecorderImportPreview(
            bundle.Manifest.BundleId,
            bundle.Manifest.DisplayName,
            bundle.Manifest.CreatedAtUtc.ToString("O"),
            _paths.RpaId,
            current.Revision.Value,
            bundle.Manifest.StepCount,
            bundle.Manifest.BlockingIssueCount,
            bundle.Manifest.WarningIssueCount,
            bundle.Manifest.HasSecrets,
            bundle.Manifest.HasUploads,
            bundle.Manifest.RecipientKeyId,
            bundle.Issues.Issues,
            timeline,
            bundle.Evidence.Items.Select(item => new RecorderEvidencePreview(
                item.Id,
                item.ActionId,
                item.Path,
                item.ThumbnailPath,
                item.Width,
                item.Height,
                item.ByteLength,
                item.Comment)).ToArray(),
            RecorderPackageMerger.RecordedInputs(bundle.Package),
            RecorderPackageMerger.SecretReferences(bundle.Package),
            RecorderPackageMerger.AttachmentReferences(bundle.Package),
            _merger.FindConflicts(current.CopyDocuments(), bundle.Package),
            bundle.Package.Locators.Locators.Select(item => item.Id).ToArray(),
            bundle.Package.Flow.Subflows.Keys.ToArray());
    }

    private static Dictionary<string, string> ParseComments(JsonElement document)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!document.TryGetProperty("comments", out var comments)) return result;
        foreach (var item in comments.EnumerateArray())
        {
            if (item.TryGetProperty("actionId", out var actionId) &&
                item.TryGetProperty("text", out var text) &&
                actionId.GetString() is { } id && text.GetString() is { } value)
            {
                result[id] = value;
            }
        }
        return result;
    }

    private static RecorderImportApplyResult ToApplyResult(
        RpaPackageSnapshot snapshot,
        IReadOnlyDictionary<string, string> remappings,
        string archive,
        bool idempotent,
        IReadOnlyList<string>? additionalWarnings = null)
    {
        var documents = snapshot.CopyDocuments();
        var warnings = RpaPackageValidator.Validate(documents).Warnings
            .Concat(additionalWarnings ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new RecorderImportApplyResult(
            snapshot.RpaId,
            snapshot.Revision.Value,
            snapshot.ContentHash,
            documents.Flow,
            documents.Locators,
            documents.Policy,
            warnings,
            remappings,
            archive,
            idempotent);
    }

    private static string ComputeRequestHash(RecorderImportApplyRequest request)
    {
        var normalized = new
        {
            request.ExpectedRevision,
            request.Mode,
            request.SubflowName,
            request.RemapConflicts,
            InputMappings = (request.InputMappings ?? new Dictionary<string, string>())
                .OrderBy(item => item.Key, StringComparer.Ordinal),
            SecretMappings = (request.SecretMappings ?? new Dictionary<string, string>())
                .OrderBy(item => item.Key, StringComparer.Ordinal),
            AttachmentMappings = (request.AttachmentMappings ?? new Dictionary<string, string>())
                .OrderBy(item => item.Key, StringComparer.Ordinal),
            ResolvedIssueIds = (request.ResolvedIssueIds ?? []).OrderBy(item => item, StringComparer.Ordinal)
        };
        return Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(normalized))).ToLowerInvariant();
    }

    private static int CountActions(FlowDefinition flow) => EnumerateActions(flow).Count();

    private static IEnumerable<FlowActionDefinition> EnumerateActions(FlowDefinition flow)
    {
        foreach (var action in Enumerate(flow.Actions)) yield return action;
        foreach (var subflow in flow.Subflows.Values)
        {
            foreach (var action in Enumerate(subflow)) yield return action;
        }

        static IEnumerable<FlowActionDefinition> Enumerate(
            IEnumerable<FlowActionDefinition> actions)
        {
            foreach (var action in actions)
            {
                yield return action;
                foreach (var nested in Enumerate(action.Actions)) yield return nested;
                foreach (var nested in Enumerate(action.ElseActions)) yield return nested;
            }
        }
    }

    private static string EvidenceRelativePath(string revision, string bundleId) =>
        Path.Combine(".recorder-imports", revision, bundleId + ".rpablockly.zip");

    private async Task TryAuditAsync(
        string operation,
        string stagingId,
        string bundleId,
        string outcome,
        string? revision,
        CancellationToken cancellationToken)
    {
        try
        {
            await _audit.WriteAsync(
                operation, stagingId, bundleId, outcome, revision, cancellationToken);
        }
        catch (IOException)
        {
            // Auditoria nunca contém dados sensíveis e não deve tornar o apply ambíguo.
        }
        catch (UnauthorizedAccessException)
        {
            // O package store continua sendo a autoridade transacional.
        }
    }
}
