using Microsoft.Playwright;
using SpyBrowser.Playwright;

namespace RpaFlow.Playwright.V2;

internal interface ICaptchaMouse
{
    Task MoveAsync(float x, float y);
    Task DownAsync();
    Task UpAsync();
}

/// <summary>
/// Arrasto substituível; a implementação Playwright sempre libera o botão
/// depois de Mouse.Down, inclusive em erro ou cancelamento.
/// </summary>
internal static class HumanizedDrag
{
    public static Task ExecuteAsync(
        IPage page,
        float fromX,
        float fromY,
        float toX,
        CancellationToken cancellationToken) =>
        ExecuteAsync(page, fromX, fromY, toX, fromY, cancellationToken);

    public static Task ExecuteAsync(
        IPage page,
        float fromX,
        float fromY,
        float toX,
        float toY,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            new PlaywrightCaptchaMouse(PlaywrightHumanizer.Unwrap(page).Mouse),
            fromX,
            fromY,
            toX,
            toY,
            cancellationToken);

    internal static async Task ExecuteAsync(
        ICaptchaMouse mouse,
        float fromX,
        float fromY,
        float toX,
        float toY,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await mouse.MoveAsync(fromX, fromY);
        await mouse.DownAsync();
        try
        {
            var steps = 16 + Random.Shared.Next(14);
            for (var index = 1; index <= steps; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var progress = index / (double)steps;
                var eased = 1 - Math.Pow(1 - progress, 3);
                var x = fromX + (toX - fromX) * (float)eased;
                var y = fromY + (toY - fromY) * (float)eased;
                var jitterY = index == steps
                    ? y
                    : y + (float)((Random.Shared.NextDouble() - 0.5) * 2.2);
                await mouse.MoveAsync(x, jitterY);
                await Task.Delay(Random.Shared.Next(6, 24), cancellationToken);
            }
        }
        finally
        {
            await mouse.UpAsync();
        }
    }

    internal static Task ExecuteAsync(
        ICaptchaMouse mouse,
        float fromX,
        float fromY,
        float toX,
        CancellationToken cancellationToken) =>
        ExecuteAsync(mouse, fromX, fromY, toX, fromY, cancellationToken);

    private sealed class PlaywrightCaptchaMouse(IMouse mouse) : ICaptchaMouse
    {
        public Task MoveAsync(float x, float y) => mouse.MoveAsync(x, y);
        public Task DownAsync() => mouse.DownAsync();
        public Task UpAsync() => mouse.UpAsync();
    }
}
