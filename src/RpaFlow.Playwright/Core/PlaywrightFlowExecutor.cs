namespace RpaFlow.Playwright;

public sealed class PlaywrightFlowExecutor : IFlowExecutor
{
    private readonly FlowDefinition _definition;
    private readonly RpaRunner _runner;

    public PlaywrightFlowExecutor(
        FlowDefinition definition,
        PlaywrightRuntimeOptions options,
        IFlowExecutionObserver? observer = null,
        IFlowActionExecutionGuard? executionGuard = null,
        IOneTimeCodeProvider? oneTimeCodeProvider = null,
        TimeProvider? timeProvider = null)
    {
        _definition = definition;
        _runner = new RpaRunner(
            FlowCompiler.Compile(definition),
            options,
            observer,
            executionGuard,
            oneTimeCodeProvider,
            timeProvider);
    }

    public Task<FlowExecutionResult> ExecuteAsync(
        FlowExecutionRequest request,
        CancellationToken cancellationToken) =>
        _runner.RunAsync(
            request,
            _definition.Inputs
                .Select(requirement => new FlowInputRequirement(
                    requirement.Path,
                    requirement.Type,
                    requirement.Required))
                .ToArray(),
            cancellationToken);
}
