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
        PlaywrightRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureSingleVisibleAsync(locator, description);
        await locator.FillWithRuntimeAsync(value, options, cancellationToken);
    }

    internal static async Task FillWithRuntimeAsync(
        this ILocator locator,
        string value,
        PlaywrightRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = locator.FillAsync(value, new LocatorFillOptions
        {
            Timeout = options.ActionTimeoutSeconds * 1_000
        });
        try
        {
            await operation.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Fechar o contexto interrompe a chamada Playwright, que não recebe token.
            try
            {
                await locator.Page.Context.CloseAsync();
            }
            catch
            {
                // O cancelamento original permanece como causa autoritativa.
            }

            try
            {
                await operation;
            }
            catch
            {
                // A chamada foi encerrada pelo fechamento do contexto.
            }
            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();
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
