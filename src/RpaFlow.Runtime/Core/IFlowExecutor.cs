namespace RpaFlow.Runtime;

public interface IFlowExecutor
{
    Task<FlowExecutionResult> ExecuteAsync(
        FlowExecutionRequest request,
        CancellationToken cancellationToken);
}
