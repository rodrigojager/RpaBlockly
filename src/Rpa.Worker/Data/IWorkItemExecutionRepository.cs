using Rpa.Worker.Domain;
using RpaFlow.Packages;
using RpaFlow.Runtime;

namespace Rpa.Worker.Data;

public interface IWorkItemExecutionRepository
{
    Task StartExecutionAsync(
        string executionId,
        RpaWorkItem workItem,
        CancellationToken cancellationToken);

    Task SetExecutionPackageAsync(
        string executionId,
        string originName,
        RpaPackageSnapshot snapshot,
        CancellationToken cancellationToken);

    Task RenewLeaseAsync(Guid workItemId, CancellationToken cancellationToken);

    Task CompleteAsync(
        string executionId,
        RpaWorkItem workItem,
        string status,
        string outputJson,
        int executedActions,
        CancellationToken cancellationToken);

    Task FailAsync(
        string executionId,
        RpaWorkItem workItem,
        WorkerFailureDecision decision,
        CancellationToken cancellationToken);

    Task SaveOutputsAsync(
        string executionId,
        RpaWorkItem workItem,
        IReadOnlyList<MaterializedOutput> outputs,
        CancellationToken cancellationToken);

    Task SaveArtifactsAsync(
        string executionId,
        RpaWorkItem workItem,
        IReadOnlyList<MaterializedArtifact> artifacts,
        CancellationToken cancellationToken);

    Task AppendEventAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken);
}
