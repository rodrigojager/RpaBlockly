namespace RpaFlow.Runtime;

public enum FlowFailureCategory
{
    Validation,
    Configuration,
    Timeout,
    FileSystem,
    BusinessRule,
    WebInteraction,
    ExternalSystem,
    Unexpected
}

public sealed record FlowExecutionFailure(
    string ExecutionId,
    string? WorkItemId,
    string? BatchId,
    FlowFailureCategory Category,
    bool Retryable,
    string Message,
    string? ActionId = null,
    string? ActionName = null,
    string? ActionType = null,
    string? CurrentUrl = null,
    DateTimeOffset? OccurredAtUtc = null);

public sealed class FlowExecutionException : Exception
{
    public FlowExecutionException(
        FlowExecutionFailure failure,
        Exception innerException)
        : base(failure.Message, innerException)
    {
        Failure = failure with
        {
            OccurredAtUtc = failure.OccurredAtUtc ?? DateTimeOffset.UtcNow
        };
    }

    public FlowExecutionFailure Failure { get; }
}
