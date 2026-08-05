using System.Diagnostics;
using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public sealed class PageActivityMonitor : IDisposable
{
    private readonly IPage _page;
    private int _activeRequests;
    private long _activityVersion;

    public PageActivityMonitor(IPage page)
    {
        _page = page;
        _page.Request += OnRequest;
        _page.RequestFinished += OnRequestCompleted;
        _page.RequestFailed += OnRequestCompleted;
    }

    public async Task WaitForIdleAsync(
        TimeSpan quietPeriod,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observedVersion = Volatile.Read(ref _activityVersion);
        var quietSince = stopwatch.Elapsed;

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentVersion = Volatile.Read(ref _activityVersion);
            var activeRequests = Math.Max(0, Volatile.Read(ref _activeRequests));

            if (currentVersion != observedVersion || activeRequests > 0)
            {
                observedVersion = currentVersion;
                quietSince = stopwatch.Elapsed;
            }
            else if (stopwatch.Elapsed - quietSince >= quietPeriod)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException(
            $"A página não ficou sem requisições por {quietPeriod.TotalMilliseconds:0} ms " +
            $"dentro do limite de {timeout.TotalSeconds:0} s.");
    }

    public void Dispose()
    {
        _page.Request -= OnRequest;
        _page.RequestFinished -= OnRequestCompleted;
        _page.RequestFailed -= OnRequestCompleted;
    }

    private void OnRequest(object? sender, IRequest request)
    {
        Interlocked.Increment(ref _activeRequests);
        Interlocked.Increment(ref _activityVersion);
    }

    private void OnRequestCompleted(object? sender, IRequest request)
    {
        if (Interlocked.Decrement(ref _activeRequests) < 0)
        {
            Interlocked.Exchange(ref _activeRequests, 0);
        }

        Interlocked.Increment(ref _activityVersion);
    }
}
