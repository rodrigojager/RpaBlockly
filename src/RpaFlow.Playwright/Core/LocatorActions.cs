using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public static class LocatorActions
{
    public static async Task ClickWhenReadyAsync(
        this ILocator locator,
        string description,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureSingleVisibleAsync(locator, description);
        await locator.ClickAsync();
    }

    public static async Task FillWhenReadyAsync(
        this ILocator locator,
        string value,
        string description,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureSingleVisibleAsync(locator, description);
        await locator.FillAsync(value);
    }

    public static async Task EnsureSingleVisibleAsync(ILocator locator, string description)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        var count = await locator.CountAsync();
        if (count != 1)
        {
            throw new InvalidOperationException(
                $"Esperado exatamente um elemento para '{description}', mas foram encontrados {count}.");
        }
    }

    public static async Task EnsureSingleAttachedAsync(ILocator locator, string description)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached
        });

        var count = await locator.CountAsync();
        if (count != 1)
        {
            throw new InvalidOperationException(
                $"Esperado exatamente um elemento para '{description}', mas foram encontrados {count}.");
        }
    }
}
