using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public static class FlowLocatorFactory
{
    public static ILocator Create(
        IPage page,
        FlowActionDefinition action,
        FlowDataContext data) =>
        Create(
            page,
            action.Selector!,
            action.Scope,
            ResolveLocatorText(
                action.ScopeHasText,
                action.ScopeHasTextSource,
                data,
                action.Name,
                "texto do escopo"),
            ResolveLocatorText(
                action.HasText,
                action.HasTextSource,
                data,
                action.Name,
                "texto do alvo"),
            action.FrameSelectors);

    public static ILocator Create(
        IPage page,
        FlowConditionDefinition condition,
        FlowDataContext data) =>
        Create(
            page,
            condition.Selector!,
            condition.Scope,
            ResolveLocatorText(
                condition.ScopeHasText,
                condition.ScopeHasTextSource,
                data,
                "condição de elemento",
                "texto do escopo"),
            ResolveLocatorText(
                condition.HasText,
            condition.HasTextSource,
                data,
                "condição de elemento",
                "texto do alvo"),
            condition.FrameSelectors);

    internal static ILocator Create(
        IFrame frame,
        FlowActionDefinition action,
        FlowDataContext data) =>
        CreateFromRoot(
            selectorValue => frame.Locator(selectorValue),
            action.Selector!,
            action.Scope,
            ResolveLocatorText(
                action.ScopeHasText,
                action.ScopeHasTextSource,
                data,
                action.Name,
                "texto do escopo"),
            ResolveLocatorText(
                action.HasText,
                action.HasTextSource,
                data,
                action.Name,
                "texto do alvo"));

    public static ILocator Create(
        IPage page,
        string selector,
        string? scope,
        string? hasText,
        IReadOnlyList<string>? frameSelectors) =>
        Create(page, selector, scope, null, hasText, frameSelectors);

    public static ILocator Create(
        IPage page,
        string selector,
        string? scope,
        string? scopeHasText,
        string? hasText,
        IReadOnlyList<string>? frameSelectors)
    {
        var frames = frameSelectors ?? [];
        if (frames.Count == 0)
        {
            return CreateFromRoot(
                selectorValue => page.Locator(selectorValue),
                selector,
                scope,
                scopeHasText,
                hasText);
        }

        var frame = page.FrameLocator(frames[0]);
        for (var index = 1; index < frames.Count; index++)
        {
            frame = frame.FrameLocator(frames[index]);
        }

        return CreateFromRoot(
            selectorValue => frame.Locator(selectorValue),
            selector,
            scope,
            scopeHasText,
            hasText);
    }

    private static ILocator CreateFromRoot(
        Func<string, ILocator> locate,
        string selector,
        string? scope,
        string? scopeHasText,
        string? hasText)
    {
        ILocator locator;
        if (string.IsNullOrWhiteSpace(scope))
        {
            locator = locate(selector);
        }
        else
        {
            var scopeLocator = locate(scope);
            if (!string.IsNullOrWhiteSpace(scopeHasText))
            {
                scopeLocator = scopeLocator.Filter(
                    new LocatorFilterOptions { HasText = scopeHasText });
            }

            locator = scopeLocator.Locator(selector);
        }

        return string.IsNullOrWhiteSpace(hasText)
            ? locator
            : locator.Filter(new LocatorFilterOptions { HasText = hasText });
    }

    private static string? ResolveLocatorText(
        string? literal,
        string? source,
        FlowDataContext data,
        string actionName,
        string description)
    {
        var resolved = FlowValueResolver.ResolveOptionalText(literal, source, data);
        if (!string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(resolved))
        {
            throw new InvalidOperationException(
                $"A ação '{actionName}' resolveu um {description} vazio em '{source}'.");
        }

        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }
}
