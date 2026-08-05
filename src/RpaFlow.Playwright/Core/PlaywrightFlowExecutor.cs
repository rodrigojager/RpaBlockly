namespace RpaFlow.Playwright;

public sealed class PlaywrightFlowExecutor : IFlowExecutor
{
    private readonly FlowDefinition _definition;
    private readonly RpaRunner _runner;

    public PlaywrightFlowExecutor(
        FlowDefinition definition,
        PlaywrightRuntimeOptions options,
        IPagePolicyFactory? pagePolicyFactory = null,
        IFlowExecutionObserver? observer = null,
        IFlowActionExecutionGuard? executionGuard = null,
        IOneTimeCodeProvider? oneTimeCodeProvider = null,
        TimeProvider? timeProvider = null)
    {
        _definition = definition;
        _runner = new RpaRunner(
            FlowCompiler.Compile(definition),
            options,
            pagePolicyFactory,
            observer,
            executionGuard,
            oneTimeCodeProvider,
            timeProvider);
    }

    public Task<FlowExecutionResult> ExecuteAsync(
        FlowExecutionRequest request,
        CancellationToken cancellationToken) =>
        _runner.RunAsync(request, _definition.Inputs, cancellationToken);
}
