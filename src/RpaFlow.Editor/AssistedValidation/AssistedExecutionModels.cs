using System.Text.Json;

namespace RpaFlow.Editor.AssistedValidation;

public sealed record AssistedExecutionStartRequest(
    string ExpectedRevision,
    JsonElement Flow,
    JsonElement Locators,
    JsonElement Policy,
    string Browser,
    string BoundaryActionId,
    bool CaptureScreenshots = true);

public sealed record AssistedExecutionEvent(
    long Sequence,
    string Kind,
    DateTimeOffset OccurredAtUtc,
    string? ActionId = null,
    string? ActionName = null,
    string? ActionType = null,
    int? ExecutedActions = null,
    long? ElapsedMilliseconds = null,
    string? FailureCategory = null,
    bool? Retryable = null,
    string? EvidenceId = null,
    string? Detail = null);

public sealed record AssistedExecutionEvidence(
    string Id,
    string Kind,
    string FileName,
    DateTimeOffset CapturedAtUtc,
    string? ActionId,
    string? ActionName);

public sealed record AssistedExecutionSnapshot(
    string ExecutionId,
    string Status,
    string Browser,
    string BoundaryActionId,
    string BoundaryActionName,
    string SourceRevision,
    string DraftHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int ExecutedActions,
    bool BoundaryReached,
    bool CanStop,
    string? Error,
    IReadOnlyList<AssistedExecutionEvent> Events,
    IReadOnlyList<AssistedExecutionEvidence> Evidence,
    IReadOnlyList<AssistedHumanHandoff> HumanHandoffs);

public sealed record AssistedHumanHandoff(
    string RequestId,
    string ExecutionId,
    string ActionId,
    string ChallengeId,
    string? Provider,
    string Kind,
    string Message,
    string State,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool EvidenceAvailable);

public sealed record AssistedHumanHandoffDecision(string Action);

public sealed record AssistedEvidenceFile(
    string Path,
    string FileName,
    string ContentType);
