namespace Rpa.Worker.Hosting;

public sealed record WorkerRuntimeSnapshot(
    bool ValidationSucceeded,
    bool ExecutionEnabled,
    int EnabledDefinitionCount,
    int MaximumParallelism,
    bool LeadershipAcquired,
    bool PollingStarted,
    bool PollingHealthy,
    bool OperationalHeartbeatHealthy,
    bool IsDraining,
    bool IsFaulted,
    int ActiveExecutions,
    int ConsecutivePollingFailures,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ValidationStartedAtUtc,
    DateTimeOffset? ValidationCompletedAtUtc,
    DateTimeOffset? LeadershipAcquiredAtUtc,
    DateTimeOffset? LeadershipHeartbeatAtUtc,
    DateTimeOffset? LeadershipLostAtUtc,
    DateTimeOffset? PollingStartedAtUtc,
    DateTimeOffset? PollingHeartbeatAtUtc,
    DateTimeOffset? LastPollingSuccessAtUtc,
    DateTimeOffset? NextPollingAtUtc,
    DateTimeOffset? LastOperationalHeartbeatAtUtc,
    DateTimeOffset? DrainingStartedAtUtc,
    DateTimeOffset? LastFailureAtUtc,
    string? LastFailureType)
{
    public int AvailableExecutionSlots =>
        Math.Max(0, MaximumParallelism - ActiveExecutions);
}

public static class WorkerRuntimeStatus
{
    public const string Starting = "Starting";
    public const string Validating = "Validating";
    public const string Disabled = "Disabled";
    public const string NoClaimDefinitions = "NoClaimDefinitions";
    public const string WaitingForLeadership = "WaitingForLeadership";
    public const string Ready = "Ready";
    public const string Processing = "Processing";
    public const string Degraded = "Degraded";
    public const string Stopping = "Stopping";
    public const string Faulted = "Faulted";
}

public sealed class WorkerRuntimeState(TimeProvider timeProvider)
{
    private readonly object _sync = new();
    private WorkerRuntimeSnapshot _snapshot = CreateInitial(timeProvider.GetUtcNow());

    public WorkerRuntimeState() : this(TimeProvider.System)
    {
    }

    public WorkerRuntimeSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    public void MarkValidationStarting() => Update(current => current with
    {
        ValidationSucceeded = false,
        ExecutionEnabled = false,
        ValidationStartedAtUtc = timeProvider.GetUtcNow(),
        ValidationCompletedAtUtc = null
    });

    public void MarkValidationPassed(bool enabled, int definitions, int parallelism) =>
        Update(current => current with
        {
            ValidationSucceeded = true,
            ExecutionEnabled = enabled,
            EnabledDefinitionCount = definitions,
            MaximumParallelism = parallelism,
            ValidationCompletedAtUtc = timeProvider.GetUtcNow(),
            IsFaulted = false
        });

    public void MarkValidationFailed(Exception exception) => Update(current => current with
    {
        ValidationSucceeded = false,
        ExecutionEnabled = false,
        ValidationCompletedAtUtc = timeProvider.GetUtcNow(),
        LastFailureAtUtc = timeProvider.GetUtcNow(),
        LastFailureType = exception.GetType().Name
    });

    public void MarkLeadershipAcquired() => Update(current => current with
    {
        LeadershipAcquired = true,
        LeadershipAcquiredAtUtc = timeProvider.GetUtcNow(),
        LeadershipHeartbeatAtUtc = timeProvider.GetUtcNow(),
        LeadershipLostAtUtc = null
    });

    public void MarkLeadershipHeartbeat() => Update(current => current with
    {
        LeadershipAcquired = true,
        LeadershipHeartbeatAtUtc = timeProvider.GetUtcNow()
    });

    public void MarkLeadershipLost(Exception? exception = null) => Update(current => current with
    {
        LeadershipAcquired = false,
        PollingHealthy = false,
        LeadershipLostAtUtc = timeProvider.GetUtcNow(),
        LastFailureAtUtc = exception is null ? current.LastFailureAtUtc : timeProvider.GetUtcNow(),
        LastFailureType = exception?.GetType().Name ?? current.LastFailureType
    });

    public void MarkPollingStarted() => Update(current => current with
    {
        PollingStarted = true,
        PollingHealthy = true,
        PollingStartedAtUtc = current.PollingStartedAtUtc ?? timeProvider.GetUtcNow(),
        PollingHeartbeatAtUtc = timeProvider.GetUtcNow(),
        ConsecutivePollingFailures = 0
    });

    public void MarkPollingSucceeded(DateTimeOffset nextPollingAtUtc) => Update(current => current with
    {
        PollingStarted = true,
        PollingHealthy = true,
        PollingStartedAtUtc = current.PollingStartedAtUtc ?? timeProvider.GetUtcNow(),
        PollingHeartbeatAtUtc = timeProvider.GetUtcNow(),
        LastPollingSuccessAtUtc = timeProvider.GetUtcNow(),
        NextPollingAtUtc = nextPollingAtUtc,
        ConsecutivePollingFailures = 0
    });

    public void MarkPollingFailed(Exception exception, DateTimeOffset nextPollingAtUtc) =>
        Update(current => current with
        {
            PollingStarted = true,
            PollingHealthy = false,
            PollingStartedAtUtc = current.PollingStartedAtUtc ?? timeProvider.GetUtcNow(),
            PollingHeartbeatAtUtc = timeProvider.GetUtcNow(),
            NextPollingAtUtc = nextPollingAtUtc,
            ConsecutivePollingFailures = current.ConsecutivePollingFailures == int.MaxValue
                ? int.MaxValue
                : current.ConsecutivePollingFailures + 1,
            LastFailureAtUtc = timeProvider.GetUtcNow(),
            LastFailureType = exception.GetType().Name
        });

    public void MarkOperationalHeartbeatPersisted() => Update(current => current with
    {
        OperationalHeartbeatHealthy = true,
        LastOperationalHeartbeatAtUtc = timeProvider.GetUtcNow()
    });

    public void MarkOperationalHeartbeatFailed(Exception exception) => Update(current => current with
    {
        OperationalHeartbeatHealthy = false,
        LastFailureAtUtc = timeProvider.GetUtcNow(),
        LastFailureType = exception.GetType().Name
    });

    public void MarkExecutionStarted() => Update(current => current with
    {
        ActiveExecutions = current.ActiveExecutions == int.MaxValue
            ? int.MaxValue
            : current.ActiveExecutions + 1
    });

    public void MarkExecutionCompleted() => Update(current => current with
    {
        ActiveExecutions = Math.Max(0, current.ActiveExecutions - 1)
    });

    public void BeginDraining() => Update(current => current with
    {
        IsDraining = true,
        DrainingStartedAtUtc = current.DrainingStartedAtUtc ?? timeProvider.GetUtcNow()
    });

    public void MarkFaulted(Exception exception) => Update(current => current with
    {
        IsFaulted = true,
        LastFailureAtUtc = timeProvider.GetUtcNow(),
        LastFailureType = exception.GetType().Name
    });

    private void Update(Func<WorkerRuntimeSnapshot, WorkerRuntimeSnapshot> update)
    {
        lock (_sync)
        {
            _snapshot = update(_snapshot) with { UpdatedAtUtc = timeProvider.GetUtcNow() };
        }
    }

    private static WorkerRuntimeSnapshot CreateInitial(DateTimeOffset now) => new(
        ValidationSucceeded: false,
        ExecutionEnabled: false,
        EnabledDefinitionCount: 0,
        MaximumParallelism: 0,
        LeadershipAcquired: false,
        PollingStarted: false,
        PollingHealthy: false,
        OperationalHeartbeatHealthy: false,
        IsDraining: false,
        IsFaulted: false,
        ActiveExecutions: 0,
        ConsecutivePollingFailures: 0,
        StartedAtUtc: now,
        UpdatedAtUtc: now,
        ValidationStartedAtUtc: null,
        ValidationCompletedAtUtc: null,
        LeadershipAcquiredAtUtc: null,
        LeadershipHeartbeatAtUtc: null,
        LeadershipLostAtUtc: null,
        PollingStartedAtUtc: null,
        PollingHeartbeatAtUtc: null,
        LastPollingSuccessAtUtc: null,
        NextPollingAtUtc: null,
        LastOperationalHeartbeatAtUtc: null,
        DrainingStartedAtUtc: null,
        LastFailureAtUtc: null,
        LastFailureType: null);
}
