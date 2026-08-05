using Microsoft.Playwright;

namespace RpaFlow.Playwright;

internal sealed class NavigationActionHandler : IFlowActionHandler
{
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "navigate",
            "click",
            "clickIfVisible",
            "wait",
            "clickAndSwitchPage",
            "switchPage",
            "closePage",
            "waitStable"
        };

    public async Task ExecuteAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "navigate":
                await NavigateAsync(action, execution.Context);
                break;
            case "click":
                await execution.CreateLocator(action)
                    .ClickWhenReadyAsync(action.Name, cancellationToken);
                break;
            case "clickifvisible":
                await ClickIfVisibleAsync(action, execution, cancellationToken);
                break;
            case "wait":
                await WaitAsync(action, execution, cancellationToken);
                break;
            case "clickandswitchpage":
                await ClickAndSwitchPageAsync(action, execution, cancellationToken);
                break;
            case "switchpage":
                await SwitchPageAsync(action, execution, cancellationToken);
                break;
            case "closepage":
                await ClosePageAsync(action, execution, cancellationToken);
                break;
            case "waitstable":
                await execution.Context.Readiness.WaitForPageToSettleAsync(
                    cancellationToken);
                break;
            default:
                throw Unsupported(action);
        }
    }

    private static async Task NavigateAsync(
        FlowActionDefinition action,
        RpaContext context)
    {
        var url = FlowValueResolver.ResolveRequired(action, context.Data);
        await context.Page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = action.TimeoutMs
        });
    }

    private static async Task ClickIfVisibleAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var locator = execution.CreateLocator(action);
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = action.TimeoutMs ?? 2_000
            });
        }
        catch (TimeoutException)
        {
            Console.WriteLine($"  Ação opcional ignorada: {action.Name}.");
            return;
        }

        await locator.ClickWhenReadyAsync(action.Name, cancellationToken);
    }

    private static async Task WaitAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = action.State?.ToLowerInvariant() switch
        {
            "attached" => WaitForSelectorState.Attached,
            "detached" => WaitForSelectorState.Detached,
            "visible" => WaitForSelectorState.Visible,
            "hidden" => WaitForSelectorState.Hidden,
            _ => throw new InvalidOperationException(
                $"Estado de espera inválido: '{action.State}'.")
        };

        try
        {
            await FlowLocatorState.WaitAsync(
                execution.CreateLocator(action),
                state,
                action.MatchMode,
                action.TimeoutMs,
                action.Name,
                cancellationToken);
        }
        catch (TimeoutException) when (action.Optional)
        {
            Console.WriteLine($"  Espera opcional expirou: {action.Name}.");
        }
        catch (TimeoutException)
        {
            await FlowFrameDiagnostics.ReportTargetMatchesAsync(
                action,
                execution.Context,
                cancellationToken);
            throw;
        }
    }

    private static async Task ClickAndSwitchPageAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var page = context.Page;
        var locator = execution.CreateLocator(action);
        await LocatorActions.EnsureSingleVisibleAsync(locator, action.Name);
        cancellationToken.ThrowIfCancellationRequested();

        var destinationPage = await page.Context.RunAndWaitForPageAsync(
            async () => await locator.ClickAsync(),
            new BrowserContextRunAndWaitForPageOptions
            {
                Timeout = action.TimeoutMs ??
                    context.Options.ActionTimeoutSeconds * 1_000
            });

        await destinationPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        context.SwitchToPage(destinationPage);
        await LocatorActions.EnsureSingleAttachedAsync(
            context.Page.Locator(action.ReadySelector!),
            $"elemento inicial da nova aba ({action.ReadySelector})");
    }

    private static async Task SwitchPageAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var expected = FlowValueResolver.ResolveRequired(action, context.Data);
        var matches = new List<IPage>();
        foreach (var page in context.Page.Context.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actual = action.Property?.ToLowerInvariant() switch
            {
                "url" => page.Url,
                "title" => await page.TitleAsync(),
                _ => throw new InvalidOperationException(
                    $"Propriedade de aba inválida em '{action.Name}': '{action.Property}'.")
            };
            if (PageMatches(actual, expected, action.Comparison))
            {
                matches.Add(page);
            }
        }

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' esperava uma aba, mas encontrou {matches.Count}.");
        }

        await matches[0].BringToFrontAsync();
        context.SwitchToPage(matches[0]);
        if (!string.IsNullOrWhiteSpace(action.ReadySelector))
        {
            await LocatorActions.EnsureSingleAttachedAsync(
                context.Page.Locator(action.ReadySelector),
                $"elemento inicial da aba ({action.ReadySelector})");
        }
    }

    private static async Task ClosePageAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = execution.Context;
        var currentPage = context.Page;
        var destinationPage = currentPage.Context.Pages
            .LastOrDefault(page => !ReferenceEquals(page, currentPage));
        if (destinationPage is null)
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' não pode fechar a única aba do contexto.");
        }

        await currentPage.CloseAsync();
        await destinationPage.BringToFrontAsync();
        context.SwitchToPage(destinationPage);
        if (!string.IsNullOrWhiteSpace(action.ReadySelector))
        {
            await LocatorActions.EnsureSingleAttachedAsync(
                context.Page.Locator(action.ReadySelector),
                $"elemento inicial após fechar aba ({action.ReadySelector})");
        }
    }

    private static bool PageMatches(
        string actual,
        string expected,
        string? comparison) =>
        comparison?.ToLowerInvariant() switch
        {
            "exact" => string.Equals(actual, expected, StringComparison.Ordinal),
            "caseinsensitive" => string.Equals(
                actual,
                expected,
                StringComparison.OrdinalIgnoreCase),
            "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static InvalidOperationException Unsupported(FlowActionDefinition action) =>
        new($"O handler de navegação não interpreta '{action.Type}'.");
}
