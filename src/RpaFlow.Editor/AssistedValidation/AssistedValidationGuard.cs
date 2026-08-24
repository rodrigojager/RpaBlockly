using RpaFlow.Playwright;
using RpaFlow.Runtime;

namespace RpaFlow.Editor.AssistedValidation;

internal sealed class AssistedValidationGuard(string boundaryActionId) :
    IFlowActionExecutionGuard
{
    private int _boundaryReached;

    public bool BoundaryReached => Volatile.Read(ref _boundaryReached) == 1;

    public ValueTask BeforeActionAsync(
        FlowActionIdentity action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask<FlowActionExecutionDirective> AfterActionAsync(
        FlowActionIdentity action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!action.Id.Equals(boundaryActionId, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(FlowActionExecutionDirective.Continue);
        }

        Volatile.Write(ref _boundaryReached, 1);
        return ValueTask.FromResult(FlowActionExecutionDirective.CompleteExecution);
    }
}
