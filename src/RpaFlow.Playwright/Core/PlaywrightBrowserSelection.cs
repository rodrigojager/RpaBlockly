namespace RpaFlow.Playwright;

public sealed record PlaywrightBrowserSelection(string Engine, string? Channel)
{
    private static readonly HashSet<string> ChromiumChannels = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "chrome-beta",
        "chrome-dev",
        "chrome-canary",
        "msedge",
        "msedge-beta",
        "msedge-dev",
        "msedge-canary"
    };

    public static IReadOnlyList<string> SupportedValues { get; } =
    [
        "chromium",
        "firefox",
        "webkit",
        "chrome",
        "chrome-beta",
        "chrome-dev",
        "chrome-canary",
        "msedge",
        "msedge-beta",
        "msedge-dev",
        "msedge-canary",
        "cloakbrowser"
    ];

    public static string SupportedValuesDescription =>
        string.Join(", ", SupportedValues);

    public static bool IsSupported(string? browser) =>
        TryResolve(browser, out _);

    public static PlaywrightBrowserSelection Resolve(string browser)
    {
        if (TryResolve(browser, out var selection))
        {
            return selection!;
        }

        throw new InvalidOperationException(
            $"Navegador não suportado: '{browser}'. Valores aceitos: " +
            SupportedValuesDescription + ".");
    }

    private static bool TryResolve(
        string? browser,
        out PlaywrightBrowserSelection? selection)
    {
        selection = null;
        if (string.IsNullOrWhiteSpace(browser))
        {
            return false;
        }

        var normalized = browser.Trim().ToLowerInvariant();
        selection = normalized switch
        {
            "chromium" => new("chromium", null),
            "firefox" => new("firefox", null),
            "webkit" => new("webkit", null),
            "cloakbrowser" => new("cloakbrowser", null),
            _ when ChromiumChannels.Contains(normalized) =>
                new("chromium", normalized),
            _ => null
        };
        return selection is not null;
    }
}
