using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rpa.Worker.Configuration;
using Rpa.Worker.Data;

namespace Rpa.Worker.Execution;

public sealed class RpaBackgroundService(
    RpaWorkerOptions options,
    SqlWorkItemRepository repository,
    WorkItemProcessor processor,
    ILogger<RpaBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = new List<Task>();
        logger.LogInformation(
            "Worker {WorkerId} iniciado em modo {ExecutionMode}, paralelismo {MaxParallelism}.",
            options.WorkerId,
            options.ExecutionMode,
            options.MaxParallelism);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                running.RemoveAll(task => task.IsCompleted);
                var claimedAny = false;
                while (running.Count < options.MaxParallelism &&
                       !stoppingToken.IsCancellationRequested)
                {
                    var workItem = await repository.ClaimNextAsync(stoppingToken);
                    if (workItem is null)
                    {
                        break;
                    }

                    claimedAny = true;
                    running.Add(ProcessSafelyAsync(workItem, stoppingToken));
                }

                if (!claimedAny)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(options.PollIntervalSeconds),
                        stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await Task.WhenAll(running);
        }
    }

    private async Task ProcessSafelyAsync(
        Domain.RpaWorkItem workItem,
        CancellationToken cancellationToken)
    {
        try
        {
            await processor.ProcessAsync(workItem, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "Falha de infraestrutura fora da política normal no item {WorkItemId}.",
                workItem.WorkItemId);
        }
    }
}
