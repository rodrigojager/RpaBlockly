using Microsoft.Playwright;

namespace RpaFlow.Playwright;

internal static class FlowLocatorState
{
    public static async Task WaitAsync(
        ILocator locator,
        WaitForSelectorState state,
        string? matchMode,
        float? timeoutMs,
        string description,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await locator.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = state,
            Timeout = timeoutMs
        });
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSingle(matchMode))
        {
            return;
        }

        var count = await locator.CountAsync();
        var validCount = state switch
        {
            WaitForSelectorState.Attached or WaitForSelectorState.Visible => count == 1,
            WaitForSelectorState.Detached => count == 0,
            WaitForSelectorState.Hidden => count <= 1,
            _ => false
        };
        if (!validCount)
        {
            throw Ambiguous(description, count, matchMode);
        }
    }

    public static async Task<bool> EvaluateAsync(
        ILocator locator,
        string state,
        string? matchMode,
        string description)
    {
        var count = await locator.CountAsync();
        if (IsSingle(matchMode) && count > 1)
        {
            throw Ambiguous(description, count, matchMode);
        }

        return state.ToLowerInvariant() switch
        {
            "attached" => IsSingle(matchMode) ? count == 1 : count > 0,
            "detached" => count == 0,
            "visible" => count > 0 && await locator.First.IsVisibleAsync(),
            "hidden" => count == 0 || !await locator.First.IsVisibleAsync(),
            _ => throw new InvalidOperationException(
                $"Estado condicional de elemento inválido: '{state}'.")
        };
    }

    private static bool IsSingle(string? matchMode) =>
        string.Equals(matchMode, "single", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException Ambiguous(
        string description,
        int count,
        string? matchMode) =>
        new(
            $"O localizador de '{description}' usa matchMode '{matchMode}', " +
            $"mas encontrou {count} elementos.");
}
