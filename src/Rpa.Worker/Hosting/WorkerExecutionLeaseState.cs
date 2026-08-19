namespace Rpa.Worker.Hosting;

public sealed record WorkerExecutionLeaseHandle(long Version, CancellationToken LeadershipLost);

public sealed class WorkerExecutionLeaseState
{
    private readonly object _sync = new();
    private TaskCompletionSource<WorkerExecutionLeaseHandle> _acquisition = NewSignal();
    private CancellationTokenSource? _leadershipLost;
    private long _version;

    public Task<WorkerExecutionLeaseHandle> WaitUntilAcquiredAsync(CancellationToken token)
    {
        lock (_sync)
        {
            return _acquisition.Task.WaitAsync(token);
        }
    }

    public void MarkAcquired()
    {
        lock (_sync)
        {
            if (_leadershipLost is not null) return;
            _leadershipLost = new CancellationTokenSource();
            _acquisition.TrySetResult(new(++_version, _leadershipLost.Token));
        }
    }

    public void MarkUnavailable()
    {
        CancellationTokenSource? lost;
        lock (_sync)
        {
            lost = _leadershipLost;
            if (lost is null) return;
            _leadershipLost = null;
            _acquisition = NewSignal();
        }

        lost.Cancel();
        lost.Dispose();
    }

    private static TaskCompletionSource<WorkerExecutionLeaseHandle> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public interface IWorkerExecutionLease
{
    Task<WorkerExecutionLeaseHandle> WaitUntilAcquiredAsync(CancellationToken token);
}
