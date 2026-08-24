using Rpa.Worker.Configuration;
using Rpa.Worker.Domain;
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
    private readonly string? _safeValidationBoundaryActionId =
        string.IsNullOrWhiteSpace(definition.SafeValidationBoundaryActionId)
            ? null
            : definition.SafeValidationBoundaryActionId.Trim();
    private int _safeValidationBoundaryReached;
    private int _irreversibleEffectCompleted;

    public bool SafeValidationBoundaryReached =>
        Volatile.Read(ref _safeValidationBoundaryReached) == 1;

    public bool IrreversibleEffectCompleted =>
        Volatile.Read(ref _irreversibleEffectCompleted) == 1;

    public ValueTask BeforeActionAsync(
        FlowActionIdentity action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (executionMode == WorkerExecutionMode.SafeValidation &&
            _irreversibleActionIds.Contains(action.Id))
        {
            if (_safeValidationBoundaryActionId is not null)
            {
                throw new InvalidOperationException(
                    $"A ação irreversível '{action.Name}' ({action.Id}) foi alcançada " +
                    $"antes do limite seguro configurado " +
                    $"'{_safeValidationBoundaryActionId}'.");
            }

            throw new SafeValidationBoundaryException(action.Id, action.Name);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<FlowActionExecutionDirective> AfterActionAsync(
        FlowActionIdentity action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (executionMode == WorkerExecutionMode.Production &&
            _irreversibleActionIds.Contains(action.Id))
        {
            Volatile.Write(ref _irreversibleEffectCompleted, 1);
        }

        if (executionMode != WorkerExecutionMode.SafeValidation ||
            _safeValidationBoundaryActionId is null ||
            !action.Id.Equals(
                _safeValidationBoundaryActionId,
                StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(FlowActionExecutionDirective.Continue);
        }

        Volatile.Write(ref _safeValidationBoundaryReached, 1);
        return ValueTask.FromResult(FlowActionExecutionDirective.CompleteExecution);
    }
}
