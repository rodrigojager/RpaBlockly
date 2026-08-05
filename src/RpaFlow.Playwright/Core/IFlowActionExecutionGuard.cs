namespace RpaFlow.Playwright;

/// <summary>
/// Executa um checkpoint obrigatório antes de uma ação do roteiro. Diferente
/// do observer de telemetria, uma falha deste guard interrompe a ação.
/// </summary>
public interface IFlowActionExecutionGuard
{
    ValueTask BeforeActionAsync(
        FlowActionDefinition action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken);
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
