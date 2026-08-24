using FlowDefinitionValidator = RpaFlow.Contracts.V2.FlowDefinitionValidator;

namespace RpaFlow.Runtime;

public sealed class FlowExecutionBudget(
    int maximumActions = FlowDefinitionValidator.MaximumStructuralActions)
{
    private int _executedActions;

    public int ExecutedActions => _executedActions;

    public void Consume(string actionName)
    {
        var current = Interlocked.Increment(ref _executedActions);
        if (current > maximumActions)
        {
            throw new InvalidOperationException(
                $"O fluxo ultrapassou o limite de {maximumActions} ações executadas " +
                $"ao chegar em '{actionName}'.");
        }
    }
}
