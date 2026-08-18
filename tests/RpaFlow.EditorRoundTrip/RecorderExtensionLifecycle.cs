using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

internal static class RecorderExtensionLifecycle
{
    public static async Task VerifyLoadedAsync(
        string extensionBuildRoot,
        string testRoot,
        string fixtureUrl)
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
        var browserErrors = new List<string>();
        context.Console += (_, message) =>
        {
            if (message.Type == "error" &&
                message.Location.StartsWith("chrome-extension://", StringComparison.Ordinal))
            {
                browserErrors.Add($"console: {message.Text}");
            }
        };
        context.WebError += (_, error) =>
        {
            if (error.Page?.Url.StartsWith("chrome-extension://", StringComparison.Ordinal) == true)
            {
                browserErrors.Add($"uncaught: {error.Error}");
            }
        };

        var probe = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        await probe.GotoAsync(fixtureUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
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
        var stateJson = await extensionPage.EvaluateAsync<string>(
            "async () => JSON.stringify(await chrome.runtime.sendMessage({ type: 'RECORDER_GET_STATE' }))");
        using var state = JsonDocument.Parse(stateJson);
        if (!state.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            throw new InvalidOperationException(
                $"O service worker MV3 não respondeu corretamente: {stateJson}");
        }
        _ = await extensionPage.EvaluateAsync<string>(
            "async () => JSON.stringify(await chrome.storage.session.get('rpablockly.e2e.probe'))");
        if (browserErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "A extensão carregada produziu erros no Chrome:\n" +
                string.Join("\n", browserErrors.Distinct(StringComparer.Ordinal)));
        }
        await context.CloseAsync();
        await VerifyWorkflowAsync(extensionBuildRoot, testRoot, fixtureUrl);
    }

    private static async Task VerifyWorkflowAsync(
        string extensionBuildRoot,
        string testRoot,
        string fixtureUrl)
    {
        var authorizedBuildRoot = await CreateAuthorizedBuildAsync(
            extensionBuildRoot,
            testRoot,
            new Uri(fixtureUrl).GetLeftPart(UriPartial.Authority));
        var profileRoot = Path.Combine(testRoot, "recorder-extension-workflow-profile");
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
                    $"--disable-extensions-except={authorizedBuildRoot}",
                    $"--load-extension={authorizedBuildRoot}"
                ]
            });
        context.SetDefaultTimeout(15_000);
        var browserErrors = new List<string>();
        context.Console += (_, message) =>
        {
            if (message.Type == "error" &&
                message.Location.StartsWith("chrome-extension://", StringComparison.Ordinal))
            {
                browserErrors.Add($"console: {message.Text}");
            }
        };
        context.WebError += (_, error) =>
        {
            if (error.Page?.Url.StartsWith("chrome-extension://", StringComparison.Ordinal) == true)
            {
                browserErrors.Add($"uncaught: {error.Error}");
            }
        };

        var targetPage = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        await targetPage.GotoAsync(fixtureUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        var extensionId = await WaitForExtensionIdAsync(context, targetPage);
        var extensionPage = await context.NewPageAsync();
        await extensionPage.GotoAsync(
            $"chrome-extension://{extensionId}/sidepanel/index.html",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await targetPage.BringToFrontAsync();
        await extensionPage.Locator("#capture-screenshots").UncheckAsync();
        await extensionPage.Locator("#privacy-accepted").CheckAsync();
        await extensionPage.Locator("#start").ClickAsync();
        await extensionPage.WaitForFunctionAsync(
            "() => document.querySelector('#status')?.textContent === 'Gravando a origem autorizada.'");
        await targetPage.Locator("#nome").FillAsync("Teste funcional do Recorder");
        await targetPage.Locator("#nome").PressAsync("Tab");
        await extensionPage.WaitForFunctionAsync(
            "async () => { const response = await chrome.runtime.sendMessage({ type: 'RECORDER_GET_STATE' }); return response.ok && response.checkpoint?.events?.length >= 2; }");
        await extensionPage.Locator("#pause").ClickAsync();
        await extensionPage.WaitForFunctionAsync(
            "() => document.querySelector('#status')?.textContent === 'Gravação pausada.'");
        await extensionPage.Locator("#resume").ClickAsync();
        await extensionPage.WaitForFunctionAsync(
            "() => document.querySelector('#status')?.textContent === 'Gravando a origem autorizada.'");
        var finalizeJson = await extensionPage.EvaluateAsync<string>(
            "async () => JSON.stringify(await chrome.runtime.sendMessage({ type: 'RECORDER_FINALIZE' }))");
        using var finalized = JsonDocument.Parse(finalizeJson);
        if (!finalized.RootElement.TryGetProperty("ok", out var finalizedOk) ||
            !finalizedOk.GetBoolean())
        {
            throw new InvalidOperationException(
                $"A validação final do Recorder falhou no Chrome: {finalizeJson}");
        }
        _ = await extensionPage.EvaluateAsync<string>(
            "async () => JSON.stringify(await chrome.runtime.sendMessage({ type: 'RECORDER_ABORT_FINALIZE' }))");
        await extensionPage.Locator("#cancel").ClickAsync();
        await extensionPage.WaitForFunctionAsync(
            "() => document.querySelector('#status')?.textContent === 'Sessão excluída.'");
        var clearedJson = await extensionPage.EvaluateAsync<string>(
            "async () => JSON.stringify(await chrome.runtime.sendMessage({ type: 'RECORDER_GET_STATE' }))");
        using var cleared = JsonDocument.Parse(clearedJson);
        if (!cleared.RootElement.TryGetProperty("ok", out var clearedOk) ||
            !clearedOk.GetBoolean() || cleared.RootElement.TryGetProperty("checkpoint", out _))
        {
            throw new InvalidOperationException(
                $"A sessão não foi limpa ao final do teste: {clearedJson}");
        }
        if (browserErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "O fluxo funcional da extensão produziu erros no Chrome:\n" +
                string.Join("\n", browserErrors.Distinct(StringComparer.Ordinal)));
        }
    }

    private static async Task<string> CreateAuthorizedBuildAsync(
        string extensionBuildRoot,
        string testRoot,
        string origin)
    {
        var destination = Path.Combine(testRoot, "recorder-extension-authorized-build");
        foreach (var directory in Directory.EnumerateDirectories(extensionBuildRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(extensionBuildRoot, directory)));
        }
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(extensionBuildRoot, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(extensionBuildRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        var manifestPath = Path.Combine(destination, "manifest.json");
        var strictUtf8 = new UTF8Encoding(false, true);
        var manifest = JsonNode.Parse(strictUtf8.GetString(await File.ReadAllBytesAsync(manifestPath)))
            ?.AsObject() ?? throw new InvalidOperationException("Manifesto de teste inválido.");
        manifest["host_permissions"] = new JsonArray(JsonValue.Create($"{origin}/*"));
        await File.WriteAllTextAsync(
            manifestPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            strictUtf8);
        return destination;
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
