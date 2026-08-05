using System.Text.Json;

namespace RpaFlow.Runtime;

public sealed record FlowExecutionEvent(
    string Kind,
    string ExecutionId,
    string? WorkItemId,
    string? BatchId,
    DateTimeOffset OccurredAtUtc,
    string? ActionId = null,
    string? ActionName = null,
    string? ActionType = null,
    int? ExecutedActions = null,
    long? ElapsedMilliseconds = null,
    FlowFailureCategory? FailureCategory = null,
    bool? Retryable = null);

public interface IFlowExecutionObserver
{
    ValueTask ObserveAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken);
}

public sealed class NullFlowExecutionObserver : IFlowExecutionObserver
{
    public static NullFlowExecutionObserver Instance { get; } = new();

    private NullFlowExecutionObserver()
    {
    }

    public ValueTask ObserveAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class ConsoleJsonFlowExecutionObserver : IFlowExecutionObserver
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ValueTask ObserveAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine(JsonSerializer.Serialize(executionEvent, SerializerOptions));
        return ValueTask.CompletedTask;
    }
}
