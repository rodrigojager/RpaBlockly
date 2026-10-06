using System.Text.Json;
using Microsoft.Playwright;
using RpaFlow.Editor.AssistedValidation;
using RpaFlow.Playwright;
using SpyBrowser.Playwright;

var assertions = 0;
void Check(bool value, string message)
{
    assertions++;
    if (!value) throw new InvalidOperationException(message);
}

var baseline = new PlaywrightRuntimeOptions(true, "spybrowser", 30, 90,
    Path.GetTempPath(), Directory.GetCurrentDirectory());
Check(baseline.SpyBrowserHumanize && baseline.SpyBrowserMouseAlgorithm == "bezier" &&
      baseline.SpyBrowserCompatibilityMode == "legacy", "Historical defaults changed.");
foreach (var algorithm in new[] { "bezier", "cursory" })
foreach (var mode in new[] { "legacy", "playwrightcompatible" })
{
    var options = BrowserLauncher.CreateHumanInteractionOptions(baseline with {
        SpyBrowserMouseAlgorithm = algorithm, SpyBrowserCompatibilityMode = mode });
    Check(options is not null, $"Valid selection {algorithm}/{mode} rejected.");
}
foreach (var bad in new[] { baseline with { SpyBrowserMouseAlgorithm = null! }, baseline with { SpyBrowserMouseAlgorithm = "bad" }, baseline with { SpyBrowserCompatibilityMode = null! }, baseline with { SpyBrowserCompatibilityMode = "bad" } })
{
    try { _ = BrowserLauncher.CreateHumanInteractionOptions(bad); throw new Exception("Invalid active selection accepted."); }
    catch (InvalidOperationException) { assertions++; }
}
Check(BrowserLauncher.CreateHumanInteractionOptions(baseline with { SpyBrowserHumanize = false, SpyBrowserMouseAlgorithm = "bad", SpyBrowserCompatibilityMode = "bad" }) is null,
    "Inactive settings were applied while humanization is off.");
PlaywrightRuntimeOptionsValidator.Validate(baseline with { Browser = "chromium", SpyBrowserMouseAlgorithm = "bad", SpyBrowserCompatibilityMode = null! });
var request = new AssistedExecutionStartRequest("rev", JsonDocument.Parse("{}").RootElement,
    JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{}").RootElement, "spybrowser", "action");
Check(request.SpyBrowserHumanize is null && request.SpyBrowserMouseAlgorithm is null && request.SpyBrowserCompatibilityMode is null,
    "Legacy assisted request must leave new overrides absent.");
Console.WriteLine($"PASS assertions={assertions}: defaults, 4 valid selections, invalid active selections, inactive gates, legacy request fallback shape.");

foreach (var (algorithm, mode, humanize) in new[] {
    ("cursory", "playwrightcompatible", true), ("bezier", "legacy", true), ("bezier", "legacy", false) })
{
    await using var session = await BrowserLauncher.LaunchAsync(baseline with {
        SpyBrowserMouseAlgorithm = algorithm, SpyBrowserCompatibilityMode = mode, SpyBrowserHumanize = humanize });
    var context = await session.Browser.NewContextAsync();
    try
    {
        var page = await context.NewPageAsync();
        Check((!ReferenceEquals(context, PlaywrightHumanizer.Unwrap(context))) == humanize,
            $"Wrapper state mismatch for {algorithm}/{mode}/{humanize}.");
        await page.SetContentAsync("<input id='field'><button id='target' style='margin:40px'>go</button><script>window.events=[];document.querySelector('#target').addEventListener('click',()=>events.push('click'));</script>");
        await page.Locator("#field").FillAsync("native fill");
        Check(await page.Locator("#field").InputValueAsync() == "native fill", "DOM fill endpoint did not receive value.");
        await page.Locator("#target").ClickAsync();
        Check(await page.EvaluateAsync<int>("window.events.length") == 1, "DOM click endpoint did not fire exactly once.");
        await page.Mouse.MoveAsync(60, 60);
        await page.Mouse.MoveAsync(130, 100);
        Check(await page.EvaluateAsync<bool>("document.elementFromPoint(130,100) !== null"), "Mouse endpoint outside DOM.");
        Check((!ReferenceEquals(page, PlaywrightHumanizer.Unwrap(page))) == humanize,
            "Page wrapping mismatch.");
        Console.WriteLine($"PASS browser launch {algorithm}/{mode}/humanize={humanize}: wrapped={humanize}, Fill/click/move endpoints observed.");
    }
    finally { await context.CloseAsync(); }
}
Console.WriteLine("PASS actual local SpyBrowser cases=3; external provider/CAPTCHA services were not used.");
