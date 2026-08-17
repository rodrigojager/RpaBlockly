namespace RpaFlow.Playwright;

public enum FlowActionExecutionDirective
{
    Continue,
    CompleteExecution
}

/// <summary>
/// Executa checkpoints obrigatórios antes e depois de uma ação do roteiro.
/// Diferente do observer de telemetria, uma falha deste guard interrompe a ação.
/// </summary>
public interface IFlowActionExecutionGuard
{
    ValueTask BeforeActionAsync(
        FlowActionDefinition action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken);

    ValueTask<FlowActionExecutionDirective> AfterActionAsync(
        FlowActionDefinition action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(FlowActionExecutionDirective.Continue);
}

public sealed class NullFlowActionExecutionGuard : IFlowActionExecutionGuard
{
    public static NullFlowActionExecutionGuard Instance { get; } = new();

    private NullFlowActionExecutionGuard()
    {
    }

    public ValueTask BeforeActionAsync(
        FlowActionDefinition action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
