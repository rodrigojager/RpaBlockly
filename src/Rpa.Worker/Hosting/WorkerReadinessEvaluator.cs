namespace Rpa.Worker.Hosting;

public sealed record WorkerReadinessAssessment(
    bool Ready,
    bool AcceptingClaims,
    string Status,
    bool LeaseHeartbeatStale,
    bool PollingHeartbeatStale,
    bool PollingDelayed);

public static class WorkerReadinessEvaluator
{
    public static WorkerReadinessAssessment Evaluate(
        WorkerRuntimeSnapshot snapshot,
        DateTimeOffset now,
        int pollingIntervalSeconds)
    {
        var pollingTolerance = TimeSpan.FromSeconds(Math.Max(60, pollingIntervalSeconds * 4 + 30));
        var leaseStale = snapshot.LeadershipAcquired &&
            (!snapshot.LeadershipHeartbeatAtUtc.HasValue ||
             now - snapshot.LeadershipHeartbeatAtUtc.Value > TimeSpan.FromSeconds(30));
        var pollingStale = snapshot.PollingStarted &&
            (!snapshot.PollingHeartbeatAtUtc.HasValue ||
             now - snapshot.PollingHeartbeatAtUtc.Value > pollingTolerance);
        var pollingDelayed = snapshot.NextPollingAtUtc.HasValue &&
            now - snapshot.NextPollingAtUtc.Value > pollingTolerance;
        var ready = snapshot.ValidationSucceeded && snapshot.ExecutionEnabled &&
            snapshot.EnabledDefinitionCount > 0 && snapshot.LeadershipAcquired &&
            snapshot.PollingStarted && snapshot.PollingHealthy && !snapshot.IsDraining &&
            !snapshot.IsFaulted && !leaseStale && !pollingStale && !pollingDelayed;
        var status = snapshot.IsFaulted ? WorkerRuntimeStatus.Faulted
            : snapshot.IsDraining ? WorkerRuntimeStatus.Stopping
            : !snapshot.ValidationSucceeded ? WorkerRuntimeStatus.Validating
            : !snapshot.ExecutionEnabled ? WorkerRuntimeStatus.Disabled
            : snapshot.EnabledDefinitionCount == 0 ? WorkerRuntimeStatus.NoClaimDefinitions
            : !snapshot.LeadershipAcquired ? WorkerRuntimeStatus.WaitingForLeadership
            : !ready ? WorkerRuntimeStatus.Degraded
            : snapshot.ActiveExecutions > 0 ? WorkerRuntimeStatus.Processing
            : WorkerRuntimeStatus.Ready;
        return new(ready, ready && snapshot.AvailableExecutionSlots > 0, status,
            leaseStale, pollingStale, pollingDelayed);
    }
}
