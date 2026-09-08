using System.Security.Cryptography;
using Microsoft.Playwright;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Resolve hCaptcha na modalidade de grade binária (image_label_binary):
/// clica o checkbox, lê o prompt e os tiles do frame de desafio, envia os
/// tiles ao serviço Python de classificação (modelos ResNet ONNX fixados),
/// clica os tiles marcados e submete. Repete até HCaptchaMaxAttempts.
/// Superfícies não-binárias (point, drag, múltipla escolha) retornam
/// Unsupported para o chamador decidir o fallback (VLM ou intervenção humana).
/// </summary>
internal static class HCaptchaSolver
{
    internal const int MaximumTiles = 16;

    public static async Task<HCaptchaSolveOutcome> ExecuteAsync(
        IPage page,
        CaptchaOptions options,
        int? maximumAttempts,
        Action<int> observeAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var anchorFrame = page.FrameLocator(
            "iframe[src*='hcaptcha'][src*='checkbox'], iframe[src*='hcaptcha'][src*='anchor']");
        var checkbox = anchorFrame.Locator("#checkbox");

        if (await IsSolvedAsync(anchorFrame, cancellationToken))
        {
            return new HCaptchaSolveOutcome(true, 0, Unsupported: false);
        }

        await checkbox.First.ClickAsync(new LocatorClickOptions { Delay = 60 });

        var challengeFrame = page.FrameLocator("iframe[src*='hcaptcha'][src*='challenge']");
        var prompt = challengeFrame.Locator(".prompt-text");
        try
        {
            await prompt.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5_000
            });
        }
        catch (TimeoutException)
        {
            if (await IsSolvedAsync(anchorFrame, cancellationToken))
            {
                return new HCaptchaSolveOutcome(true, 0, Unsupported: false);
            }
            throw new CaptchaException(
                CaptchaErrorCodes.ExtractionFailed,
                "O desafio visual do hCaptcha não foi exibido após o clique no checkbox.",
                retryable: true);
        }

        using var client = new CaptchaServiceClient(options);
        var maximum = ResolveMaximumAttempts(options, maximumAttempts);
        var attemptsUsed = 0;
        var deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(options.DeadlineSeconds);
        while (attemptsUsed < maximum)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsSolvedAsync(anchorFrame, cancellationToken))
            {
                return new HCaptchaSolveOutcome(true, attemptsUsed, Unsupported: false);
            }

            try
            {
                await prompt.First.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 5_000
                });
            }
            catch (TimeoutException)
            {
                if (await IsSolvedAsync(anchorFrame, cancellationToken))
                {
                    return new HCaptchaSolveOutcome(true, attemptsUsed, Unsupported: false);
                }
                throw new CaptchaException(
                    CaptchaErrorCodes.ExtractionFailed,
                    "O desafio hCaptcha não apresentou uma nova rodada dentro do prazo.",
                    retryable: true,
                    attempts: attemptsUsed);
            }

            var promptText = (await prompt.First.InnerTextAsync()).Trim();
            var tiles = challengeFrame.Locator(".task-grid .image");
            var tileCount = await tiles.CountAsync();
            if (tileCount is 0 || tileCount > MaximumTiles)
            {
                return new HCaptchaSolveOutcome(false, attemptsUsed, Unsupported: true);
            }

            var tileShots = new byte[tileCount][];
            for (var index = 0; index < tileCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tileShots[index] = await tiles.Nth(index).ScreenshotAsync(
                    new LocatorScreenshotOptions { Type = ScreenshotType.Png });
            }

            var snapshotId = Convert.ToHexString(SHA256.HashData(Concat(tileShots)));
            CaptchaSolveResult inference;
            try
            {
                inference = await client.SolveV2Async(
                    new CaptchaServiceSolveRequest(
                        Guid.NewGuid().ToString("N"),
                        $"hcaptcha-{snapshotId[..16].ToLowerInvariant()}",
                        snapshotId,
                        "hcaptcha_image_label",
                        new Dictionary<string, object?>
                        {
                            ["tilesBase64"] = tileShots
                                .Select(shot => Convert.ToBase64String(shot))
                                .ToArray()
                        },
                        new Dictionary<string, object?>
                        {
                            ["tileCount"] = tileCount
                        },
                        promptText,
                        deadlineUtc,
                        new Dictionary<string, object?>
                        {
                            ["maxAttempts"] = maximum - attemptsUsed,
                            ["localOnly"] = true,
                            ["allowVlmFallback"] = false
                        },
                        Provider: "hcaptcha",
                        Kind: CaptchaKind.HCaptcha),
                    cancellationToken);
            }
            catch (CaptchaException exception)
            {
                var composedAttempts = V2CaptchaActionHandler.ComposeAttempts(
                    attemptsUsed,
                    exception.Attempts);
                observeAttempts(composedAttempts);
                throw new CaptchaException(
                    exception.ErrorCode,
                    exception.Message,
                    exception.Retryable,
                    exception,
                    composedAttempts);
            }
            attemptsUsed = V2CaptchaActionHandler.ComposeAttempts(
                attemptsUsed,
                inference.Attempts);
            observeAttempts(attemptsUsed);
            var decisions = inference.TileDecisions ?? throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "O serviço V2 não produziu decisões de tile para o desafio hCaptcha.",
                attempts: attemptsUsed);

            foreach (var decision in decisions.Where(item => item.Match))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await tiles.Nth(decision.Index).ClickAsync(
                    new LocatorClickOptions { Delay = 40 });
            }

            await challengeFrame
                .Locator(".button-submit")
                .First.ClickAsync(new LocatorClickOptions { Delay = 80 });

            if (await IsSolvedAsync(anchorFrame, cancellationToken, timeoutMs: 6_000))
            {
                return new HCaptchaSolveOutcome(true, attemptsUsed, Unsupported: false);
            }

            // rodada não aceita ou nova rodada de tiles: o loop reclassifica.
        }

        return new HCaptchaSolveOutcome(false, attemptsUsed, Unsupported: false);
    }

    internal static int ResolveMaximumAttempts(
        CaptchaOptions options,
        int? actionMaximumAttempts) =>
        Math.Clamp(actionMaximumAttempts ?? options.HCaptchaMaxAttempts, 1, 10);

    private static async Task<bool> IsSolvedAsync(
        IFrameLocator anchorFrame,
        CancellationToken cancellationToken,
        float timeoutMs = 4_000)
    {
        try
        {
            await anchorFrame
                .Locator("#checkbox[aria-checked='true']")
                .WaitForAsync(
                    new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Attached,
                        Timeout = timeoutMs
                    });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static byte[] Concat(IReadOnlyList<byte[]> parts)
    {
        using var output = new MemoryStream();
        foreach (var part in parts)
        {
            output.Write(part, 0, part.Length);
        }
        return output.ToArray();
    }
}

internal sealed record HCaptchaSolveOutcome(bool Solved, int Attempts, bool Unsupported);
