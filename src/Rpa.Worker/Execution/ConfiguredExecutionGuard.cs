using Rpa.Worker.Configuration;
using Rpa.Worker.Domain;
using RpaFlow.Contracts;
using RpaFlow.Playwright;
using RpaFlow.Runtime;

namespace Rpa.Worker.Execution;

public sealed class ConfiguredExecutionGuard(
    WorkerExecutionMode executionMode,
    RpaDefinitionOptions definition) : IFlowActionExecutionGuard
{
    private readonly HashSet<string> _irreversibleActionIds = new(
        definition.IrreversibleActionIds,
        StringComparer.OrdinalIgnoreCase);

    public ValueTask BeforeActionAsync(
        FlowActionDefinition action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (executionMode == WorkerExecutionMode.SafeValidation &&
            _irreversibleActionIds.Contains(action.Id))
        {
            throw new SafeValidationBoundaryException(action.Id, action.Name);
        }

        return ValueTask.CompletedTask;
    }
}
