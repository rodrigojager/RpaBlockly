using CloakBrowser;
using Microsoft.Playwright;
using SpyBrowser.Core;
using SpyBrowser.Playwright;

namespace RpaFlow.Playwright;

/// <summary>
/// Cria o navegador da execução a partir das opções do runtime, encapsulando a
/// diferença entre Playwright, SpyBrowser e CloakBrowser. O restante do runtime
/// continua usando somente as interfaces do Microsoft.Playwright.
/// </summary>
public static class BrowserLauncher
{
    /// <summary>
    /// Build público e gratuito do CloakBrowser (Chromium 146), anterior ao
    /// modelo Free/Pro. O pino faz o wrapper baixar esse binário diretamente
    /// do GitHub Releases, sem chave de licença, e impede que uma execução
    /// resolva silenciosamente para o canal mais recente, que exige licença.
    /// </summary>
    public const string CloakBrowserBinaryVersion = "146.0.7680.177.5";

    public static async Task<BrowserSession> LaunchAsync(
        PlaywrightRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = PlaywrightBrowserSelection.Resolve(options.Browser);
        if (selection.Engine.Equals("spybrowser", StringComparison.OrdinalIgnoreCase))
        {
            var identity = BrowserIdentity.Create("rpablockly", "RpaBlockly") with
            {
                Locale = options.Locale,
                TimezoneId = ResolveLocalTimeZoneId(),
                Viewport = new ViewportIdentity
                {
                    Width = options.ViewportWidth,
                    Height = options.ViewportHeight,
                    ScreenWidth = Math.Max(1920, options.ViewportWidth),
                    ScreenHeight = Math.Max(1080, options.ViewportHeight)
                },
                Browser = new BrowserIdentitySettings
                {
                    Engine = BrowserEngine.Chromium,
                    Channel = null
                }
            };
            var handle = await AwaitCancellableResourceAsync(
                SpyBrowserLauncher.LaunchBrowserAsync(
                    new SpyBrowserLaunchOptions
                    {
                        IdentityId = identity.Id,
                        IdentityOverride = identity,
                        Headless = options.Headless,
                        Humanize = options.SpyBrowserHumanize,
                        DefaultTimeoutMilliseconds = options.ActionTimeoutSeconds * 1_000,
                        DefaultNavigationTimeoutMilliseconds = options.ActionTimeoutSeconds * 1_000,
                        RunGpuProbe = false
                    },
                    cancellationToken),
                static async launched => await launched.DisposeAsync(),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                await handle.DisposeAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return new BrowserSession(handle);
        }
        if (selection.Engine.Equals("cloakbrowser", StringComparison.OrdinalIgnoreCase))
        {
            var handle = await AwaitCancellableResourceAsync(
                CloakLauncher.LaunchAsync(new LaunchOptions
                {
                    Headless = options.Headless,
                    Locale = options.Locale,
                    BrowserVersion = CloakBrowserBinaryVersion
                }),
                static async launched => await launched.DisposeAsync(),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                await handle.DisposeAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return new BrowserSession(handle);
        }

        var playwright = await AwaitCancellableResourceAsync(
            Microsoft.Playwright.Playwright.CreateAsync(),
            static created =>
            {
                created.Dispose();
                return Task.CompletedTask;
            },
            cancellationToken);
        try
        {
            var browserType = ResolveBrowserType(playwright, selection.Engine);
            cancellationToken.ThrowIfCancellationRequested();
            var browser = await AwaitCancellableResourceAsync(
                browserType.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = options.Headless,
                    Channel = selection.Channel
                }),
                static launched => launched.CloseAsync(),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                await browser.CloseAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return new BrowserSession(playwright, browser);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    internal static string ResolveLocalTimeZoneId()
    {
        var localId = TimeZoneInfo.Local.Id;
        return OperatingSystem.IsWindows() &&
            TimeZoneInfo.TryConvertWindowsIdToIanaId(localId, out var ianaId)
                ? ianaId
                : localId;
    }

    internal static async Task<T> AwaitCancellableResourceAsync<T>(
        Task<T> operation,
        Func<T, Task> cleanupAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = operation.ContinueWith(
                    async completed =>
                    {
                        if (completed.Status == TaskStatus.RanToCompletion)
                        {
                            try
                            {
                                await cleanupAsync(completed.Result);
                            }
                            catch
                            {
                                // O chamador já recebeu o cancelamento; limpeza é best effort.
                            }
                        }
                        else if (completed.IsFaulted)
                        {
                            _ = completed.Exception;
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default)
                .Unwrap();
            throw;
        }
    }

    private static IBrowserType ResolveBrowserType(IPlaywright playwright, string browser) =>
        browser.ToLowerInvariant() switch
        {
            "chromium" => playwright.Chromium,
            "firefox" => playwright.Firefox,
            "webkit" => playwright.Webkit,
            _ => throw new InvalidOperationException($"Navegador não suportado: {browser}")
        };
}

/// <summary>
/// Posse do navegador e dos recursos que o sustentam. O descarte fecha o
/// provider e libera seus recursos na ordem correta.
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    private readonly IPlaywright? _playwright;
    private readonly CloakBrowserHandle? _cloakHandle;
    private readonly SpyBrowserBrowserHandle? _spyBrowserHandle;

    internal BrowserSession(IPlaywright playwright, IBrowser browser)
    {
        _playwright = playwright;
        Browser = browser;
    }

    internal BrowserSession(CloakBrowserHandle cloakHandle)
    {
        _cloakHandle = cloakHandle;
        Browser = cloakHandle.RawBrowser;
    }

    internal BrowserSession(SpyBrowserBrowserHandle spyBrowserHandle)
    {
        _spyBrowserHandle = spyBrowserHandle;
        Browser = new SpyBrowserSessionBrowser(spyBrowserHandle);
    }

    public IBrowser Browser { get; }

    public Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions options) =>
        Browser.NewContextAsync(options);

    public async ValueTask DisposeAsync()
    {
        if (_cloakHandle is not null)
        {
            await _cloakHandle.DisposeAsync();
            return;
        }
        if (_spyBrowserHandle is not null)
        {
            await _spyBrowserHandle.DisposeAsync();
            return;
        }

        await Browser.CloseAsync();
        _playwright?.Dispose();
    }
}

internal sealed class SpyBrowserSessionBrowser(SpyBrowserBrowserHandle handle) : IBrowser
{
    private readonly IBrowser _browser = handle.Browser;
    private readonly object _contextsLock = new();
    private readonly List<IBrowserContext> _contexts = [];
    private EventHandler<IBrowserContext>? _context;

    public event EventHandler<IBrowserContext>? Context
    {
        add => _context += value;
        remove => _context -= value;
    }

    public event EventHandler<IBrowser>? Disconnected
    {
        add => _browser.Disconnected += value;
        remove => _browser.Disconnected -= value;
    }

    public IBrowserType BrowserType => _browser.BrowserType;
    public IReadOnlyList<IBrowserContext> Contexts
    {
        get
        {
            lock (_contextsLock)
            {
                return _contexts.ToArray();
            }
        }
    }
    public bool IsConnected => _browser.IsConnected;
    public string Version => _browser.Version;

    public Task CloseAsync(BrowserCloseOptions? options = null) => _browser.CloseAsync(options);
    public Task<ICDPSession> NewBrowserCDPSessionAsync() => _browser.NewBrowserCDPSessionAsync();
    public async Task<IBrowserContext> NewContextAsync(
        BrowserNewContextOptions? options = null)
    {
        var context = await handle.NewContextAsync(options);
        Track(context);
        return context;
    }

    public async Task<IPage> NewPageAsync(BrowserNewPageOptions? options = null)
    {
        var page = await handle.NewPageAsync(options);
        Track(page.Context);
        return page;
    }
    public Task<BrowserBindResult> BindAsync(
        string wsEndpoint,
        BrowserBindOptions? options = null) =>
        _browser.BindAsync(wsEndpoint, options);
    public Task UnbindAsync() => _browser.UnbindAsync();
    public ValueTask DisposeAsync() => _browser.DisposeAsync();

    private void Track(IBrowserContext context)
    {
        var closed = 0;
        context.Close += (_, _) =>
        {
            Interlocked.Exchange(ref closed, 1);
            lock (_contextsLock)
            {
                _contexts.Remove(context);
            }
        };

        EventHandler<IBrowserContext>? handler;
        lock (_contextsLock)
        {
            if (Volatile.Read(ref closed) != 0 || _contexts.Contains(context))
            {
                return;
            }
            _contexts.Add(context);
            handler = _context;
        }

        if (Volatile.Read(ref closed) == 0)
        {
            handler?.Invoke(this, context);
        }
    }
}
