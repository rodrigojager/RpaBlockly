using System.Text.Json.Nodes;

namespace Rpa.Worker.Domain;

public sealed record RpaWorkItem(
    Guid WorkItemId,
    string RpaCode,
    string? BatchId,
    string? SessionKey,
    int AttemptCount,
    int MaxAttempts,
    string InputJson,
    string ConfigurationJson,
    string AttachmentsJson);

public sealed record MaterializedOutput(
    string Name,
    JsonNode? Value,
    bool Sensitive);

public sealed record MaterializedArtifact(
    string Name,
    string Kind,
    string Path,
    long SizeBytes,
    string Sha256);

public sealed class SafeValidationBoundaryException(string actionId, string actionName)
    : InvalidOperationException(
        $"A validação segura parou antes da ação irreversível '{actionName}' ({actionId}).")
{
    public string ActionId { get; } = actionId;

    public string ActionName { get; } = actionName;
}
