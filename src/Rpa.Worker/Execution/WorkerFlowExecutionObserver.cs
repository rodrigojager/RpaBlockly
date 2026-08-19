using Rpa.Worker.Data;
using RpaFlow.Runtime;

namespace Rpa.Worker.Execution;

public sealed class WorkerFlowExecutionObserver(
    SqlWorkItemRepository repository,
    IEnumerable<string> authenticationAttemptActionIds,
    IEnumerable<string> mfaAttemptActionIds) : IFlowExecutionObserver
{
    private readonly HashSet<string> _authenticationAttemptActionIds =
        authenticationAttemptActionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _mfaAttemptActionIds =
        mfaAttemptActionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private string? _activeActionId;
    private int _authenticationAttemptStarted;
    private int _authenticationAttemptCompleted;
    private int _mfaAttemptStarted;

    public string? ActiveActionId => Volatile.Read(ref _activeActionId);
    public bool AuthenticationAttemptStarted => Volatile.Read(ref _authenticationAttemptStarted) == 1;
    public bool AuthenticationAttemptCompleted => Volatile.Read(ref _authenticationAttemptCompleted) == 1;
    public bool MfaAttemptStarted => Volatile.Read(ref _mfaAttemptStarted) == 1;

    public async ValueTask ObserveAsync(FlowExecutionEvent executionEvent, CancellationToken token)
    {
        Track(executionEvent);
        await repository.AppendEventAsync(executionEvent, token);
    }

    internal void Track(FlowExecutionEvent executionEvent)
    {
        if (executionEvent.Kind.Equals("actionStarted", StringComparison.Ordinal))
        {
            Volatile.Write(ref _activeActionId, executionEvent.ActionId);
            if (executionEvent.ActionId is not null &&
                _authenticationAttemptActionIds.Contains(executionEvent.ActionId))
            {
                Volatile.Write(ref _authenticationAttemptStarted, 1);
                Volatile.Write(ref _authenticationAttemptCompleted, 0);
            }
            if (executionEvent.ActionId is not null && _mfaAttemptActionIds.Contains(executionEvent.ActionId))
                Volatile.Write(ref _mfaAttemptStarted, 1);
        }
        else if (executionEvent.Kind is "actionCompleted" or "actionFailed" &&
                 executionEvent.ActionId?.Equals(ActiveActionId, StringComparison.OrdinalIgnoreCase) == true)
        {
            Volatile.Write(ref _activeActionId, null);
        }

        if (executionEvent.Kind.Equals("actionCompleted", StringComparison.Ordinal) &&
            executionEvent.ActionType?.Equals(
                "completeAuthenticationAttempt", StringComparison.OrdinalIgnoreCase) == true)
            Volatile.Write(ref _authenticationAttemptCompleted, 1);
    }
}
