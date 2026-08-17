using RpaFlow.Packages;
using RpaFlow.Playwright.V2.Adaptive;
using RpaFlow.Runtime;

namespace RpaFlow.Playwright.V2;

public sealed class PlaywrightV2FlowExecutor : IFlowExecutor
{
    private readonly RpaFlow.Contracts.V2.FlowDefinition _definition;
    private readonly RpaRunner _runner;
    private readonly LocatorLearningManager? _learning;
    private readonly IFlowExecutionObserver _observer;
    private readonly RpaPackageSnapshot _snapshot;

    public PlaywrightV2FlowExecutor(
        RpaPackageSnapshot snapshot,
        PlaywrightRuntimeOptions options,
        IFlowExecutionObserver? observer = null,
        IFlowActionExecutionGuard? executionGuard = null,
        IOneTimeCodeProvider? oneTimeCodeProvider = null,
        TimeProvider? timeProvider = null,
        IRpaPackageWriter? sourceWriteBack = null,
        IRpaPackageWriter? overlayWriteBack = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _observer = observer ?? NullFlowExecutionObserver.Instance;
        var flow = snapshot.Flow;
        var locators = snapshot.Locators;
        var policy = snapshot.Policy;
        _definition = flow;
        var writeBack = policy.LocatorResilience.LearningWriteBack switch
        {
            RpaFlow.Contracts.V2.LearningWriteBackMode.Source => sourceWriteBack,
            RpaFlow.Contracts.V2.LearningWriteBackMode.Overlay => overlayWriteBack,
            _ => null
        };
        _learning = policy.LocatorResilience.Mode ==
            RpaFlow.Contracts.V2.LocatorResilienceMode.Adaptive
                ? new LocatorLearningManager(snapshot, writeBack)
                : null;
        var resolver = new LocatorResolver(
            locators,
            policy,
            learning: _learning,
            observer: _observer,
            snapshot: snapshot);
        _runner = new RpaRunner(
            V2FlowCompiler.Compile(flow, resolver),
            options,
            _observer,
            executionGuard,
            oneTimeCodeProvider,
            timeProvider);
    }

    public async Task<FlowExecutionResult> ExecuteAsync(
        FlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        _learning?.Begin(request.ExecutionId);
        try
        {
            var result = await _runner.RunAsync(
                request,
                _definition.Inputs.Select(FlowInputRequirement.From).ToArray(),
                cancellationToken);
            if (_learning is not null)
            {
                var completion = await _learning.CompleteAsync(
                    request.ExecutionId,
                    succeeded: true,
                    CancellationToken.None);
                await ObserveLearningCompletionAsync(request, completion);
            }

            return result;
        }
        catch
        {
            if (_learning is not null)
            {
                var completion = await _learning.CompleteAsync(
                    request.ExecutionId,
                    succeeded: false,
                    CancellationToken.None);
                await ObserveLearningCompletionAsync(request, completion);
            }

            throw;
        }
    }

    private async Task ObserveLearningCompletionAsync(
        FlowExecutionRequest request,
        LocatorLearningCompletion completion)
    {
        var kind = completion.Status switch
        {
            LocatorLearningCompletionStatus.ConfirmedInMemory or
                LocatorLearningCompletionStatus.Persisted => "locatorPromotionCompleted",
            LocatorLearningCompletionStatus.Discarded => "locatorPromotionDiscarded",
            LocatorLearningCompletionStatus.RevisionConflict => "locatorPromotionConflict",
            LocatorLearningCompletionStatus.PersistenceFailed => "locatorPromotionFailed",
            _ => "locatorLearningCompleted"
        };
        foreach (var observation in completion.Observations ?? [])
        {
            try
            {
                await _observer.ObserveAsync(
                    new FlowExecutionEvent(
                        kind,
                        request.ExecutionId,
                        request.WorkItemId,
                        request.BatchId,
                        DateTimeOffset.UtcNow,
                        RpaId: _snapshot.RpaId,
                        PackageOrigin: _snapshot.Origin.Kind,
                        PackageRevision: completion.Revision?.Value ??
                            _snapshot.Revision.Value,
                        PackageHash: _snapshot.ContentHash,
                        LocatorId: observation.LocatorId,
                        CandidateId: observation.Candidate.Id,
                        ResolutionReason: completion.Status.ToString(),
                        Detail: completion.Detail),
                    CancellationToken.None);
            }
            catch
            {
                // Observabilidade não pode alterar o resultado da execução.
            }
        }
    }
}
