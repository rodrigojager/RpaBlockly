using CloakBrowser;
using Microsoft.Playwright;

namespace RpaFlow.Playwright;

/// <summary>
/// Cria o navegador da execução a partir das opções do runtime, encapsulando a
/// diferença entre os motores gerenciados pelo Playwright e o binário stealth
/// do CloakBrowser. O restante do runtime continua usando somente as
/// interfaces do Microsoft.Playwright.
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
        PlaywrightRuntimeOptions options)
    {
        var selection = PlaywrightBrowserSelection.Resolve(options.Browser);
        if (selection.Engine.Equals("cloakbrowser", StringComparison.OrdinalIgnoreCase))
        {
            var handle = await CloakLauncher.LaunchAsync(new LaunchOptions
            {
                Headless = options.Headless,
                Locale = options.Locale,
                BrowserVersion = CloakBrowserBinaryVersion
            });
            return new BrowserSession(handle);
        }

        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        try
        {
            var browserType = ResolveBrowserType(playwright, selection.Engine);
            var browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = options.Headless,
                Channel = selection.Channel
            });
            return new BrowserSession(playwright, browser);
        }
        catch
        {
            playwright.Dispose();
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
/// Posse do navegador e dos recursos que o sustentam (driver do Playwright ou
/// handle do CloakBrowser). O descarte fecha o navegador e libera esses
/// recursos na ordem correta.
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    private readonly IPlaywright? _playwright;
    private readonly CloakBrowserHandle? _cloakHandle;

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

    public IBrowser Browser { get; }

    public async ValueTask DisposeAsync()
    {
        if (_cloakHandle is not null)
        {
            await _cloakHandle.DisposeAsync();
            return;
        }

        await Browser.CloseAsync();
        _playwright?.Dispose();
    }
}
