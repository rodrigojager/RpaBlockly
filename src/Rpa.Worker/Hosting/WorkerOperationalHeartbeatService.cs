using Rpa.Worker.Configuration;
using Rpa.Worker.Data;
using Rpa.Worker.Domain;

namespace Rpa.Worker.Hosting;

public sealed class WorkerOperationalHeartbeatService(
    RpaWorkerOptions options,
    WorkerStartupValidationState validationState,
    WorkerRuntimeState runtimeState,
    SqlWorkItemRepository repository,
    TimeProvider timeProvider,
    ILogger<WorkerOperationalHeartbeatService> logger) : BackgroundService
{
    private readonly Guid _instanceId = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await validationState.WaitUntilValidatedAsync(stoppingToken);
        if (!options.Enabled) return;
        await PersistAsync(false, stoppingToken);
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.OperationalHeartbeatSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PersistAsync(false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (options.Enabled && runtimeState.GetSnapshot().ValidationSucceeded)
            await PersistAsync(true, cancellationToken);
    }

    private async Task PersistAsync(bool finalized, CancellationToken token)
    {
        var snapshot = runtimeState.GetSnapshot();
        var assessment = WorkerReadinessEvaluator.Evaluate(
            snapshot, timeProvider.GetUtcNow(), options.PollIntervalSeconds);
        var heartbeat = new WorkerOperationalHeartbeat(
            _instanceId, options.WorkerId, Environment.MachineName, Environment.ProcessId,
            finalized ? WorkerRuntimeStatus.Stopping : assessment.Status,
            !finalized && assessment.Ready, !finalized && assessment.AcceptingClaims,
            snapshot.ExecutionEnabled, snapshot.LeadershipAcquired, snapshot.PollingHealthy,
            snapshot.ActiveExecutions, snapshot.MaximumParallelism,
            finalized ? 0 : snapshot.AvailableExecutionSlots, snapshot.StartedAtUtc,
            snapshot.LeadershipHeartbeatAtUtc, snapshot.PollingHeartbeatAtUtc,
            snapshot.LastPollingSuccessAtUtc, snapshot.NextPollingAtUtc,
            snapshot.LastFailureAtUtc, snapshot.LastFailureType, finalized);
        try
        {
            await repository.RecordWorkerHeartbeatAsync(heartbeat, token);
            runtimeState.MarkOperationalHeartbeatPersisted();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            runtimeState.MarkOperationalHeartbeatFailed(exception);
            logger.LogError(exception, "Não foi possível persistir o heartbeat operacional.");
        }
    }
}
