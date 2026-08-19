using Rpa.Worker.Configuration;

namespace Rpa.Worker.Hosting;

public static class WorkerHostingEndpoints
{
    public static IEndpointRouteBuilder MapWorkerHostingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Ok(new { service = "Rpa.Worker", health = new { live = "/health/live", ready = "/health/ready" } }));
        endpoints.MapGet("/health/live", () => Results.Ok(new { service = "Rpa.Worker", status = "Healthy" }));
        endpoints.MapGet("/health/ready", GetReadiness);
        return endpoints;
    }

    private static IResult GetReadiness(
        HttpContext context,
        WorkerRuntimeState state,
        TimeProvider timeProvider,
        RpaWorkerOptions options)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        var snapshot = state.GetSnapshot();
        var assessment = WorkerReadinessEvaluator.Evaluate(
            snapshot, timeProvider.GetUtcNow(), options.PollIntervalSeconds);
        var payload = new
        {
            service = "Rpa.Worker",
            status = assessment.Status,
            ready = assessment.Ready,
            acceptingClaims = assessment.AcceptingClaims,
            snapshot.ValidationSucceeded,
            snapshot.ExecutionEnabled,
            snapshot.EnabledDefinitionCount,
            snapshot.LeadershipAcquired,
            leaseHeartbeatStale = assessment.LeaseHeartbeatStale,
            snapshot.PollingStarted,
            snapshot.PollingHealthy,
            pollingHeartbeatStale = assessment.PollingHeartbeatStale,
            pollingDelayed = assessment.PollingDelayed,
            snapshot.OperationalHeartbeatHealthy,
            draining = snapshot.IsDraining,
            faulted = snapshot.IsFaulted,
            snapshot.ActiveExecutions,
            snapshot.MaximumParallelism,
            snapshot.AvailableExecutionSlots,
            snapshot.ConsecutivePollingFailures,
            snapshot.StartedAtUtc,
            snapshot.UpdatedAtUtc,
            snapshot.ValidationStartedAtUtc,
            snapshot.ValidationCompletedAtUtc,
            snapshot.LeadershipHeartbeatAtUtc,
            snapshot.PollingHeartbeatAtUtc,
            snapshot.LastPollingSuccessAtUtc,
            snapshot.NextPollingAtUtc,
            snapshot.LastOperationalHeartbeatAtUtc,
            snapshot.LastFailureAtUtc,
            snapshot.LastFailureType
        };
        return Results.Json(payload, statusCode: assessment.Ready ? 200 : 503);
    }
}
