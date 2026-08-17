using Microsoft.Playwright;
using RpaFlow.Contracts.V2;
using RpaFlow.Runtime;

namespace RpaFlow.Playwright.V2;

public sealed class LocatorRecipeCompiler
{
    public ILocator Compile(
        IPage page,
        LocatorRecipe recipe,
        FlowDataContext data)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(data);

        IFrameLocator? frame = null;
        foreach (var frameExpression in recipe.Frames)
        {
            var frameElement = frame is null
                ? Locate(page, frameExpression)
                : Locate(frame, frameExpression);
            frame = frameElement.ContentFrame;
        }

        ILocator? scope = null;
        if (recipe.Scope is not null)
        {
            scope = frame is null
                ? Locate(page, recipe.Scope)
                : Locate(frame, recipe.Scope);
            scope = ApplyTextFilter(scope, recipe.Scope.HasText, data);
        }

        var target = scope is not null
            ? Locate(scope, recipe.Target)
            : frame is null
                ? Locate(page, recipe.Target)
                : Locate(frame, recipe.Target);
        return ApplyTextFilter(target, recipe.Target.HasText, data);
    }

    public ILocator CompileAdaptiveCandidateSet(
        IPage page,
        LocatorRecipe recipe,
        FlowDataContext data)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(data);

        IFrameLocator? frame = null;
        foreach (var frameExpression in recipe.Frames)
        {
            if (frameExpression.Strategy == LocatorStrategy.Fingerprint)
            {
                throw new InvalidOperationException(
                    "Frames não podem depender de fingerprint adaptativo.");
            }

            var frameElement = frame is null
                ? Locate(page, frameExpression)
                : Locate(frame, frameExpression);
            frame = frameElement.ContentFrame;
        }

        if (recipe.Scope is not null)
        {
            if (recipe.Scope.Strategy == LocatorStrategy.Fingerprint)
            {
                throw new InvalidOperationException(
                    "Scope não pode depender de fingerprint adaptativo.");
            }

            var scope = frame is null
                ? Locate(page, recipe.Scope)
                : Locate(frame, recipe.Scope);
            return ApplyTextFilter(scope, recipe.Scope.HasText, data).Locator("*");
        }

        return frame is null ? page.Locator("html *") : frame.Locator("html *");
    }

    private static ILocator Locate(IPage root, LocatorExpression expression) =>
        expression.Strategy switch
        {
            LocatorStrategy.Css => root.Locator("css=" + expression.Selector),
            LocatorStrategy.XPath => root.Locator("xpath=" + expression.Selector),
            LocatorStrategy.RawPlaywright => root.Locator(expression.Selector!),
            LocatorStrategy.Role => root.GetByRole(
                ParseRole(expression.Role!),
                new PageGetByRoleOptions
                {
                    NameString = expression.Name,
                    Exact = expression.Exact
                }),
            LocatorStrategy.Label => root.GetByLabel(
                expression.Text!,
                new PageGetByLabelOptions { Exact = expression.Exact }),
            LocatorStrategy.Placeholder => root.GetByPlaceholder(
                expression.Text!,
                new PageGetByPlaceholderOptions { Exact = expression.Exact }),
            LocatorStrategy.Text => root.GetByText(
                expression.Text!,
                new PageGetByTextOptions { Exact = expression.Exact }),
            LocatorStrategy.TestId => root.GetByTestId(expression.Text!),
            LocatorStrategy.Fingerprint => throw UnsupportedFingerprint(expression),
            _ => throw new ArgumentOutOfRangeException(
                nameof(expression),
                expression.Strategy,
                "Estratégia de locator desconhecida.")
        };

    private static ILocator Locate(IFrameLocator root, LocatorExpression expression) =>
        expression.Strategy switch
        {
            LocatorStrategy.Css => root.Locator("css=" + expression.Selector),
            LocatorStrategy.XPath => root.Locator("xpath=" + expression.Selector),
            LocatorStrategy.RawPlaywright => root.Locator(expression.Selector!),
            LocatorStrategy.Role => root.GetByRole(
                ParseRole(expression.Role!),
                new FrameLocatorGetByRoleOptions
                {
                    NameString = expression.Name,
                    Exact = expression.Exact
                }),
            LocatorStrategy.Label => root.GetByLabel(
                expression.Text!,
                new FrameLocatorGetByLabelOptions { Exact = expression.Exact }),
            LocatorStrategy.Placeholder => root.GetByPlaceholder(
                expression.Text!,
                new FrameLocatorGetByPlaceholderOptions { Exact = expression.Exact }),
            LocatorStrategy.Text => root.GetByText(
                expression.Text!,
                new FrameLocatorGetByTextOptions { Exact = expression.Exact }),
            LocatorStrategy.TestId => root.GetByTestId(expression.Text!),
            LocatorStrategy.Fingerprint => throw UnsupportedFingerprint(expression),
            _ => throw new ArgumentOutOfRangeException(
                nameof(expression),
                expression.Strategy,
                "Estratégia de locator desconhecida.")
        };

    private static ILocator Locate(ILocator root, LocatorExpression expression) =>
        expression.Strategy switch
        {
            LocatorStrategy.Css => root.Locator("css=" + expression.Selector),
            LocatorStrategy.XPath => root.Locator("xpath=" + expression.Selector),
            LocatorStrategy.RawPlaywright => root.Locator(expression.Selector!),
            LocatorStrategy.Role => root.GetByRole(
                ParseRole(expression.Role!),
                new LocatorGetByRoleOptions
                {
                    NameString = expression.Name,
                    Exact = expression.Exact
                }),
            LocatorStrategy.Label => root.GetByLabel(
                expression.Text!,
                new LocatorGetByLabelOptions { Exact = expression.Exact }),
            LocatorStrategy.Placeholder => root.GetByPlaceholder(
                expression.Text!,
                new LocatorGetByPlaceholderOptions { Exact = expression.Exact }),
            LocatorStrategy.Text => root.GetByText(
                expression.Text!,
                new LocatorGetByTextOptions { Exact = expression.Exact }),
            LocatorStrategy.TestId => root.GetByTestId(expression.Text!),
            LocatorStrategy.Fingerprint => throw UnsupportedFingerprint(expression),
            _ => throw new ArgumentOutOfRangeException(
                nameof(expression),
                expression.Strategy,
                "Estratégia de locator desconhecida.")
        };

    private static ILocator ApplyTextFilter(
        ILocator locator,
        LocatorTextConstraint? constraint,
        FlowDataContext data)
    {
        if (constraint is null)
        {
            return locator;
        }

        var text = FlowValueResolver.ResolveOptionalText(
            constraint.Literal,
            constraint.Source,
            data);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"O filtro de texto resolveu valor vazio em '{constraint.Source}'.");
        }

        return locator.Filter(new LocatorFilterOptions { HasText = text });
    }

    private static AriaRole ParseRole(string role)
    {
        if (!Enum.TryParse<AriaRole>(role, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException($"ARIA role inválida: '{role}'.");
        }

        return parsed;
    }

    private static NotSupportedException UnsupportedFingerprint(
        LocatorExpression expression) =>
        new(
            $"A estratégia fingerprint '{expression.FingerprintId}' exige o modo adaptativo.");
}
