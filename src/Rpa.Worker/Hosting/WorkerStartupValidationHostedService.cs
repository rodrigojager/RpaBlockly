using Rpa.Worker.Configuration;
using Rpa.Worker.Data;

namespace Rpa.Worker.Hosting;

public sealed class WorkerStartupValidationHostedService(
    RpaWorkerOptions options,
    SqlWorkItemRepository repository,
    WorkerStartupValidationState validationState,
    WorkerRuntimeState runtimeState,
    ILogger<WorkerStartupValidationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            runtimeState.MarkValidationStarting();
            try
            {
                if (options.Enabled) await repository.ValidateSchemaAsync(stoppingToken);
                runtimeState.MarkValidationPassed(
                    options.Enabled,
                    options.Definitions.Count(item => item.Value.Enabled && item.Value.ClaimEnabled),
                    options.MaxParallelism);
                validationState.MarkValidated();
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                runtimeState.MarkValidationFailed(exception);
                logger.LogError(exception,
                    "A validação operacional falhou; o host continuará vivo e repetirá em {Delay}s.",
                    retryDelay.TotalSeconds);
            }

            await Task.Delay(retryDelay, stoppingToken);
            retryDelay = TimeSpan.FromSeconds(Math.Min(60, retryDelay.TotalSeconds * 2));
        }
    }
}
