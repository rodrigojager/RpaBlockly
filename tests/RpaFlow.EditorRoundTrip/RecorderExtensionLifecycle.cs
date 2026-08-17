using System.Text.Json;
using Microsoft.Playwright;

internal static class RecorderExtensionLifecycle
{
    public static async Task VerifyLoadedAsync(
        string extensionBuildRoot,
        string testRoot)
    {
        var profileRoot = Path.Combine(testRoot, "recorder-extension-profile");
        Directory.CreateDirectory(profileRoot);
        using var playwright = await Playwright.CreateAsync();
        await using var context = await playwright.Chromium.LaunchPersistentContextAsync(
            profileRoot,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = true,
                Channel = "chromium",
                Args =
                [
                    $"--disable-extensions-except={extensionBuildRoot}",
                    $"--load-extension={extensionBuildRoot}"
                ]
            });
        context.SetDefaultTimeout(15_000);

        var probe = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        var extensionId = await WaitForExtensionIdAsync(context, probe);
        var extensionPage = await context.NewPageAsync();
        await extensionPage.GotoAsync(
            $"chrome-extension://{extensionId}/sidepanel/index.html",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var manifestJson = await extensionPage.EvaluateAsync<string>(
            "() => JSON.stringify(chrome.runtime.getManifest())");
        using var manifest = JsonDocument.Parse(manifestJson);
        var root = manifest.RootElement;
        if (root.GetProperty("name").GetString() != "RpaBlockly Recorder V2" ||
            root.GetProperty("manifest_version").GetInt32() != 3 ||
            root.GetProperty("background").GetProperty("service_worker").GetString() !=
                "background/service-worker.js")
        {
            throw new InvalidOperationException(
                "O Chromium não carregou o manifesto MV3 esperado do Recorder.");
        }
        if (await extensionPage.TitleAsync() != "RpaBlockly Recorder V2" ||
            !await extensionPage.Locator("#start").IsVisibleAsync() ||
            !await extensionPage.Locator("#privacy-accepted").IsVisibleAsync())
        {
            throw new InvalidOperationException(
                "O side panel empacotado não abriu com os controles de consentimento.");
        }
        _ = await extensionPage.EvaluateAsync<string>(
            "async () => JSON.stringify(await chrome.storage.session.get('rpablockly.e2e.probe'))");
    }

    private static async Task<string> WaitForExtensionIdAsync(
        IBrowserContext context,
        IPage probe)
    {
        var session = await context.NewCDPSessionAsync(probe);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var targets = await session.SendAsync("Target.getTargets");
            if (targets is not null &&
                targets.Value.TryGetProperty("targetInfos", out var targetInfos))
            {
                foreach (var target in targetInfos.EnumerateArray())
                {
                    if (target.GetProperty("type").GetString() != "service_worker") continue;
                    var url = target.GetProperty("url").GetString();
                    if (url is not null &&
                        url.StartsWith("chrome-extension://", StringComparison.Ordinal) &&
                        url.EndsWith("/background/service-worker.js", StringComparison.Ordinal))
                    {
                        return new Uri(url).Host;
                    }
                }
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("O service worker MV3 do Recorder não iniciou.");
    }
}
