using Rpa.Worker.Data;
using Rpa.Worker.Domain;
using RpaFlow.Runtime;

namespace Rpa.Worker.Execution;

public sealed class DatabaseFlowExecutionObserver(
    IWorkItemExecutionRepository repository,
    RpaWorkItem workItem)
    : IFlowExecutionObserver
{
    public ValueTask ObserveAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken) =>
        new(repository.AppendEventAsync(executionEvent, workItem, cancellationToken));
}
