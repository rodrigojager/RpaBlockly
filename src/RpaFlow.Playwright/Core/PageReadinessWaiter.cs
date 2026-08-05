using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public sealed class PageReadinessWaiter
{
    private const string StabilityKey = "__rpaFlowStableForm";

    private static readonly string[] DefaultBusySelectors =
    [
        "[aria-busy='true']",
        "[data-loading='true']",
        ".loading",
        ".spinner",
        ".spinner-border",
        ".turbo-progress-bar"
    ];

    private readonly IPage _page;
    private readonly PageActivityMonitor _activityMonitor;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _quietPeriod;
    private readonly TimeSpan _formStabilityPeriod;
    private readonly IReadOnlyList<string> _busySelectors;

    public PageReadinessWaiter(
        IPage page,
        PageActivityMonitor activityMonitor,
        TimeSpan timeout,
        TimeSpan quietPeriod,
        TimeSpan formStabilityPeriod,
        IReadOnlyList<string>? busySelectors)
    {
        _page = page;
        _activityMonitor = activityMonitor;
        _timeout = timeout;
        _quietPeriod = quietPeriod;
        _formStabilityPeriod = formStabilityPeriod;
        _busySelectors = busySelectors ?? DefaultBusySelectors;

        if (_quietPeriod < TimeSpan.FromMilliseconds(50) ||
            _quietPeriod > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException(
                "ReadinessQuietPeriodMs deve estar entre 50 e 60000.");
        }

        if (_formStabilityPeriod < TimeSpan.FromMilliseconds(50) ||
            _formStabilityPeriod > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException(
                "FormStabilityMs deve estar entre 50 e 60000.");
        }

        if (_busySelectors.Count > 50 ||
            _busySelectors.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "BusySelectors deve possuir no máximo 50 seletores CSS não vazios.");
        }
    }

    public async Task UploadAndWaitAsync(
        ILocator fileInput,
        string filePath,
        string description,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await LocatorActions.EnsureSingleAttachedAsync(fileInput, description);
        await _page.EvaluateAsync($"delete window.{StabilityKey}");
        await fileInput.SetInputFilesAsync(filePath);

        await _activityMonitor.WaitForIdleAsync(
            _quietPeriod,
            _timeout,
            cancellationToken);

        await WaitForStableFormAsync(cancellationToken);
    }

    public async Task WaitForPageToSettleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _page.EvaluateAsync($"delete window.{StabilityKey}");
        await _activityMonitor.WaitForIdleAsync(
            _quietPeriod,
            _timeout,
            cancellationToken);
        await WaitForStableFormAsync(cancellationToken);
    }

    private async Task WaitForStableFormAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _page.WaitForFunctionAsync(
            $$"""
            options => {
              const isVisible = element => {
                const style = window.getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return style.visibility !== 'hidden' && style.display !== 'none' &&
                  rect.width > 0 && rect.height > 0;
              };

              const hasVisibleLoading = options.busySelectors.some(selector =>
                Array.from(document.querySelectorAll(selector)).some(isVisible));

              const fields = Array.from(document.querySelectorAll(
                "form input:not([type='file']), form select, form textarea"));
              const snapshot = JSON.stringify(fields.map(field => ({
                id: field.id,
                name: field.name,
                value: field.value,
                disabled: field.disabled
              })));
              const now = performance.now();
              const previous = window.{{StabilityKey}};

              if (hasVisibleLoading || !previous || previous.snapshot !== snapshot) {
                window.{{StabilityKey}} = { snapshot, since: now };
                return false;
              }

              return now - previous.since >= options.formStabilityMs;
            }
            """,
            new
            {
                busySelectors = _busySelectors,
                formStabilityMs = _formStabilityPeriod.TotalMilliseconds
            },
            new PageWaitForFunctionOptions
            {
                PollingInterval = 100,
                Timeout = (float)_timeout.TotalMilliseconds
            });
    }
}
