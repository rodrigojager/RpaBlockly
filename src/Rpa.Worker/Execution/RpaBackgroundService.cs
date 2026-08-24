using Rpa.Worker.Configuration;
using Rpa.Worker.Data;
using Rpa.Worker.Domain;
using Rpa.Worker.Hosting;

namespace Rpa.Worker.Execution;

public sealed class RpaBackgroundService(
    RpaWorkerOptions options,
    SqlWorkItemRepository repository,
    WorkItemProcessor processor,
    WorkerStartupValidationState validationState,
    IWorkerExecutionLease executionLease,
    WorkerRuntimeState runtimeState,
    TimeProvider timeProvider,
    ILogger<RpaBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await validationState.WaitUntilValidatedAsync(stoppingToken);
        if (!options.Enabled)
        {
            await WaitUntilStoppedAsync(stoppingToken);
            return;
        }

        logger.LogInformation(
            "Worker {WorkerId} iniciado em modo {ExecutionMode}, paralelismo {MaxParallelism}.",
            options.WorkerId,
            options.ExecutionMode,
            options.MaxParallelism);
        while (!stoppingToken.IsCancellationRequested)
        {
            WorkerExecutionLeaseHandle lease;
            try
            {
                lease = await executionLease.WaitUntilAcquiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunLeadershipSessionAsync(lease, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        runtimeState.BeginDraining();
        await base.StopAsync(cancellationToken);
    }

    private async Task RunLeadershipSessionAsync(
        WorkerExecutionLeaseHandle lease,
        CancellationToken stoppingToken)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken, lease.LeadershipLost);
        var active = new List<Task>();
        runtimeState.MarkPollingStarted();
        try
        {
            while (!session.IsCancellationRequested)
            {
                active.RemoveAll(task => task.IsCompleted);
                Exception? pollingFailure = null;
                var claimedAny = false;
                while (active.Count < options.MaxParallelism && !session.IsCancellationRequested)
                {
                    RpaWorkItem? item;
                    try
                    {
                        item = await repository.ClaimNextAsync(session.Token);
                    }
                    catch (OperationCanceledException) when (session.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        pollingFailure = exception;
                        logger.LogError(exception,
                            "Falha ao consultar a fila; o polling continuará no próximo ciclo.");
                        break;
                    }

                    if (item is null) break;
                    claimedAny = true;
                    active.Add(ProcessTrackedAsync(item, stoppingToken, lease.LeadershipLost));
                }

                var next = timeProvider.GetUtcNow().AddSeconds(options.PollIntervalSeconds);
                if (pollingFailure is null) runtimeState.MarkPollingSucceeded(next);
                else runtimeState.MarkPollingFailed(pollingFailure, next);
                var delay = Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), session.Token);
                if (active.Count > 0) await Task.WhenAny(active.Append(delay));
                else if (!claimedAny) await delay;
            }
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (Exception exception)
        {
            runtimeState.MarkPollingFailed(
                exception, timeProvider.GetUtcNow().AddSeconds(options.PollIntervalSeconds));
            logger.LogError(exception, "O ciclo de polling será reiniciado.");
        }
        finally
        {
            try { await Task.WhenAll(active); }
            catch (Exception exception)
            {
                logger.LogError(exception, "Execução ativa falhou durante o drain.");
            }
        }
    }

    private async Task ProcessTrackedAsync(
        RpaWorkItem item,
        CancellationToken stoppingToken,
        CancellationToken leadershipLostToken)
    {
        runtimeState.MarkExecutionStarted();
        try { await processor.ProcessAsync(item, stoppingToken, leadershipLostToken); }
        catch (Exception exception)
        {
            logger.LogCritical(exception,
                "Falha de infraestrutura fora da política normal no item {WorkItemId}.",
                item.WorkItemId);
        }
        finally { runtimeState.MarkExecutionCompleted(); }
    }

    private static async Task WaitUntilStoppedAsync(CancellationToken token)
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
