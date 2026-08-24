using System.Text.Json;
using RpaFlow.Contracts.Recorder;
using RpaFlow.Contracts.V2;
using RpaFlow.Packages;

namespace RpaFlow.Editor.Recorder;

public enum RecorderImportMode
{
    Replace,
    AppendMain,
    Subflow
}

public sealed record RecorderImportConflict(
    string Code,
    string Path,
    string Existing,
    string Incoming,
    string ProposedResolution,
    bool Blocking);

public sealed record RecorderTimelineItem(
    string EventId,
    string ActionId,
    string ActionType,
    string ActionName,
    string? LocatorId,
    string? EvidenceId,
    string? Comment);

public sealed record RecorderEvidencePreview(
    string Id,
    string ActionId,
    string Path,
    string ThumbnailPath,
    int Width,
    int Height,
    long ByteLength,
    string? Comment);

public sealed record RecorderImportPreview(
    string BundleId,
    string DisplayName,
    string CreatedAtUtc,
    string TargetRpaId,
    string TargetRevision,
    int StepCount,
    int BlockingIssueCount,
    int WarningIssueCount,
    bool HasSecrets,
    bool HasUploads,
    string? RecipientKeyId,
    IReadOnlyList<RecorderIssue> Issues,
    IReadOnlyList<RecorderTimelineItem> Timeline,
    IReadOnlyList<RecorderEvidencePreview> Evidence,
    IReadOnlyList<string> RecordedInputPaths,
    IReadOnlyList<string> SecretReferences,
    IReadOnlyList<string> AttachmentReferences,
    IReadOnlyList<RecorderImportConflict> Conflicts,
    IReadOnlyList<string> ImportedLocatorIds,
    IReadOnlyList<string> ImportedSubflows);

public sealed record RecorderInspectResult(
    string StagingId,
    string StagingToken,
    DateTimeOffset ExpiresAtUtc,
    RecorderImportPreview Preview);

public sealed record RecorderImportApplyRequest(
    string ExpectedRevision,
    RecorderImportMode Mode,
    string? SubflowName,
    bool RemapConflicts,
    IReadOnlyDictionary<string, string> InputMappings,
    IReadOnlyDictionary<string, string> SecretMappings,
    IReadOnlyDictionary<string, string> AttachmentMappings,
    IReadOnlyList<string> ResolvedIssueIds);

public sealed record RecorderImportValidationResult(
    bool CanApply,
    string ExpectedRevision,
    RecorderImportMode Mode,
    string ResultingFlowName,
    int ResultingActionCount,
    int ResultingLocatorCount,
    IReadOnlyDictionary<string, string> IdRemappings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record RecorderImportApplyResult(
    string RpaId,
    string Revision,
    string ContentHash,
    FlowDefinition Flow,
    LocatorCatalog Locators,
    RpaPolicyDefinition Policy,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> IdRemappings,
    string EvidenceArchive,
    bool IdempotentReplay);

internal sealed record InspectedRecorderBundle(
    RecorderBundleManifest Manifest,
    RecorderSessionDocument Session,
    RecorderEvidenceDocument Evidence,
    RecorderIssuesDocument Issues,
    RecorderIntegrityDocument Integrity,
    RpaPackageDocuments Package,
    JsonElement Events,
    JsonElement Comments,
    JsonElement Samples,
    byte[] ArchiveBytes,
    IReadOnlyDictionary<string, byte[]> EntryBytes);

internal sealed record RecorderMergeResult(
    RpaPackageDocuments Documents,
    IReadOnlyDictionary<string, string> IdRemappings,
    IReadOnlyList<string> Warnings);
