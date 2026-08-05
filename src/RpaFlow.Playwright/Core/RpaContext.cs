using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public sealed class RpaContext : IDisposable
{
    private readonly IPagePolicyFactory _pagePolicyFactory;
    private readonly IFlowExecutionObserver _observer;
    private readonly IFlowActionExecutionGuard _executionGuard;
    private PageActivityMonitor? _activityMonitor;
    private IPagePolicy? _pagePolicy;

    public RpaContext(
        IPage page,
        PlaywrightRuntimeOptions options,
        FlowExecutionRequest executionRequest,
        string outputDirectory,
        IPagePolicyFactory? pagePolicyFactory = null,
        IFlowExecutionObserver? observer = null,
        IFlowActionExecutionGuard? executionGuard = null,
        IOneTimeCodeProvider? oneTimeCodeProvider = null,
        TimeProvider? timeProvider = null)
    {
        Options = options;
        Data = new FlowDataContext(executionRequest);
        ExecutionRequest = executionRequest;
        Page = page;
        _pagePolicyFactory = pagePolicyFactory ?? DefaultPagePolicyFactory.Instance;
        _observer = observer ?? NullFlowExecutionObserver.Instance;
        _executionGuard = executionGuard ?? NullFlowActionExecutionGuard.Instance;
        OneTimeCodeProvider = oneTimeCodeProvider;
        TimeProvider = timeProvider ?? TimeProvider.System;
        Artifacts = new ExecutionArtifacts(
            page,
            outputDirectory,
            executionRequest.ExecutionId);
        SwitchToPage(page);
    }

    public IPage Page { get; private set; }

    public PlaywrightRuntimeOptions Options { get; }

    public FlowDataContext Data { get; }

    public FlowExecutionRequest ExecutionRequest { get; }

    public IOneTimeCodeProvider? OneTimeCodeProvider { get; }

    public TimeProvider TimeProvider { get; }

    public FlowExecutionBudget ExecutionBudget { get; } = new();

    public PageReadinessWaiter Readiness { get; private set; } = null!;

    public IPagePolicy PagePolicy { get; private set; } = null!;

    public ExecutionArtifacts Artifacts { get; }

    public async ValueTask ObserveAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await _observer.ObserveAsync(executionEvent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Observador de execução falhou sem interromper o RPA: {exception.Message}");
        }
    }

    public ValueTask GuardBeforeActionAsync(
        FlowActionDefinition action,
        CancellationToken cancellationToken) =>
        _executionGuard.BeforeActionAsync(
            action,
            ExecutionRequest,
            cancellationToken);

    public void SwitchToPage(IPage newPage)
    {
        ArgumentNullException.ThrowIfNull(newPage);

        _activityMonitor?.Dispose();
        _pagePolicy?.Dispose();

        Page = newPage;
        Page.SetDefaultTimeout(Options.ActionTimeoutSeconds * 1_000);
        Page.SetDefaultNavigationTimeout(Options.ActionTimeoutSeconds * 1_000);

        _activityMonitor = new PageActivityMonitor(Page);
        PagePolicy = _pagePolicy = _pagePolicyFactory.Create(Page);
        Readiness = new PageReadinessWaiter(
            Page,
            _activityMonitor,
            TimeSpan.FromSeconds(Options.UploadTimeoutSeconds),
            TimeSpan.FromMilliseconds(Options.ReadinessQuietPeriodMs),
            TimeSpan.FromMilliseconds(Options.FormStabilityMs),
            Options.BusySelectors);
        Artifacts.SwitchPage(Page);
    }

    public void Dispose()
    {
        _activityMonitor?.Dispose();
        _pagePolicy?.Dispose();
    }
}
