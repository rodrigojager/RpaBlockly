using System.Text.Json.Nodes;

namespace RpaFlow.Runtime;

public sealed record FlowExecutionRequest(
    string ExecutionId,
    JsonObject Input,
    JsonObject Configuration,
    JsonObject Attachments,
    string? WorkItemId = null,
    string? BatchId = null);

public sealed record FlowExecutionResult(
    string ExecutionId,
    string? WorkItemId,
    string? BatchId,
    JsonObject Output,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    int ExecutedActions = 0);
