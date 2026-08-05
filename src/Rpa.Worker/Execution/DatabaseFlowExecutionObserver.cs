using Rpa.Worker.Data;
using RpaFlow.Runtime;

namespace Rpa.Worker.Execution;

public sealed class DatabaseFlowExecutionObserver(SqlWorkItemRepository repository)
    : IFlowExecutionObserver
{
    public ValueTask ObserveAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken) =>
        new(repository.AppendEventAsync(executionEvent, cancellationToken));
}
