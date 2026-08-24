using Microsoft.Playwright;
using RpaFlow.Contracts.V2;
using RpaFlow.Runtime.V2;
using FlowActionDefinition = RpaFlow.Contracts.V2.FlowActionDefinition;

namespace RpaFlow.Playwright.V2;

internal sealed class V2NavigationActionHandler : IV2FlowActionHandler
{
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "navigate", "click", "clickIfVisible", "wait",
            "clickAndSwitchPage", "switchPage", "closePage", "waitStable"
        };

    public async Task ExecuteAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "navigate":
                await NavigateAsync(action, execution.Context);
                return;
            case "click":
                await ClickAsync(action, execution, cancellationToken);
                return;
            case "clickifvisible":
                await ClickIfVisibleAsync(action, execution, cancellationToken);
                return;
            case "wait":
                await WaitAsync(action, execution, cancellationToken);
                return;
            case "clickandswitchpage":
                await ClickAndSwitchPageAsync(action, execution, cancellationToken);
                return;
            case "switchpage":
                await SwitchPageAsync(action, execution, cancellationToken);
                return;
            case "closepage":
                await ClosePageAsync(action, execution, cancellationToken);
                return;
            case "waitstable":
                await execution.Context.Readiness.WaitForPageToSettleAsync(cancellationToken);
                return;
            default:
                throw new InvalidOperationException(
                    $"O handler V2 de navegação não interpreta '{action.Type}'.");
        }
    }

    private static async Task NavigateAsync(
        FlowActionDefinition action,
        RpaContext context)
    {
        var url = V2FlowValueResolver.ResolveRequired(action, context.Data);
        try
        {
            await context.Page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = action.TimeoutMs
                });
        }
        catch (TimeoutException)
        {
            if (!await HasUsableDocumentAsync(context.Page))
            {
                throw;
            }

            Console.WriteLine(
                $"  A página de '{action.Name}' já está utilizável; " +
                "a espera por DOMContentLoaded foi encerrada pelo limite de tempo.");
        }
    }

    private static async Task<bool> HasUsableDocumentAsync(IPage page)
    {
        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var current) ||
            current.Scheme is not ("http" or "https"))
        {
            return false;
        }

        try
        {
            return await page.EvaluateAsync<bool>(
                """
                () => Boolean(
                  document.body &&
                  (document.readyState === 'interactive' ||
                   document.readyState === 'complete' ||
                   document.body.childElementCount > 0 ||
                   document.body.innerText.trim().length > 0))
                """);
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task ClickAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var target = await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await target.Locator.ClickAsync();
    }

    private static async Task ClickIfVisibleAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        try
        {
            var target = await execution.ResolveAsync(
                action.Target!,
                LocatorRequiredState.Visible,
                cancellationToken,
                action.TimeoutMs ?? 2_000);
            await target.Locator.ClickAsync();
        }
        catch (LocatorResolutionException exception)
            when (exception.Attempts.All(attempt =>
                attempt.FailureReason is LocatorResolutionFailureReason.NotFound or
                    LocatorResolutionFailureReason.Timeout or
                    LocatorResolutionFailureReason.InvalidState))
        {
            Console.WriteLine($"  Ação opcional ignorada: {action.Name}.");
        }
    }

    private static async Task WaitAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var state = action.State?.ToLowerInvariant() switch
        {
            "attached" => LocatorRequiredState.Attached,
            "visible" => LocatorRequiredState.Visible,
            "detached" => LocatorRequiredState.Detached,
            "hidden" => LocatorRequiredState.Hidden,
            _ => throw new InvalidOperationException(
                $"Estado de espera inválido: '{action.State}'.")
        };
        try
        {
            _ = await execution.ResolveTargetAsync(
                action,
                state,
                cancellationToken,
                allowEmpty: state is LocatorRequiredState.Detached or LocatorRequiredState.Hidden);
        }
        catch (LocatorResolutionException) when (action.Optional)
        {
            Console.WriteLine($"  Espera opcional expirou: {action.Name}.");
        }
    }

    private static async Task ClickAndSwitchPageAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var target = await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken);
        var destinationPage = await context.Page.Context.RunAndWaitForPageAsync(
            () => target.Locator.ClickAsync(),
            new BrowserContextRunAndWaitForPageOptions
            {
                Timeout = action.TimeoutMs ??
                    context.Options.ActionTimeoutSeconds * 1_000
            });
        await destinationPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        context.SwitchToPage(destinationPage);
        await ResolveReadyAsync(action, execution, cancellationToken, required: true);
    }

    private static async Task SwitchPageAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var expected = V2FlowValueResolver.ResolveRequired(action, context.Data);
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
        await ResolveReadyAsync(action, execution, cancellationToken, required: false);
    }

    private static async Task ClosePageAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = execution.Context;
        var currentPage = context.Page;
        var destinationPage = currentPage.Context.Pages
            .LastOrDefault(page => !ReferenceEquals(page, currentPage)) ??
            throw new InvalidOperationException(
                $"A ação '{action.Name}' não pode fechar a única aba do contexto.");
        await currentPage.CloseAsync();
        await destinationPage.BringToFrontAsync();
        context.SwitchToPage(destinationPage);
        await ResolveReadyAsync(action, execution, cancellationToken, required: false);
    }

    private static async Task ResolveReadyAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken,
        bool required)
    {
        if (action.Ready is null)
        {
            if (required)
            {
                throw new InvalidOperationException(
                    $"A ação '{action.Name}' exige o locator ready.");
            }

            return;
        }

        _ = await execution.ResolveAsync(
            action.Ready,
            LocatorRequiredState.Attached,
            cancellationToken,
            action.TimeoutMs);
    }

    private static bool PageMatches(string actual, string expected, string? comparison) =>
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
}
