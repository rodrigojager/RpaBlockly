using System.Diagnostics;
using Microsoft.Playwright;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Observa o lifecycle do widget na sessão original. Não produz tokens e só
/// executa o clique único do Turnstile quando a ação o autoriza explicitamente.
/// </summary>
internal static class SamePageCaptchaAdapter
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PassiveTurnstileWindow = TimeSpan.FromSeconds(1);

    public static async Task<CaptchaSolveResult> ExecuteAsync(
        IPage page,
        DetectedCaptcha detected,
        bool allowInteractiveClick,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(detected);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var stopwatch = Stopwatch.StartNew();
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var actions = new List<CaptchaPlannedAction>();
        string? evidence = detected.Challenge.Kind switch
        {
            CaptchaKind.CloudflareTurnstile => await WaitForTurnstileAsync(
                page,
                detected,
                allowInteractiveClick,
                deadline,
                actions,
                cancellationToken),
            CaptchaKind.FriendlyCaptcha => await WaitForFriendlyCaptchaAsync(
                page,
                detected,
                deadline,
                cancellationToken),
            CaptchaKind.CloudflareChallenge => await WaitForManagedChallengeAsync(
                page,
                deadline,
                cancellationToken),
            _ => throw new CaptchaException(
                CaptchaErrorCodes.UnsupportedType,
                $"O adapter same-page não interpreta {detected.Challenge.Kind}.")
        };

        if (evidence is null)
        {
            return new CaptchaSolveResult
            {
                Status = CaptchaSolveStatus.NeedsHuman,
                Provider = detected.Challenge.Provider,
                Kind = detected.Challenge.Kind,
                SolverId = SolverId(detected.Challenge.Kind),
                Actions = actions,
                ErrorCode = CaptchaErrorCodes.DeadlineExceeded,
                ErrorMessage = "O lifecycle do desafio não concluiu dentro do prazo observado.",
                Retryable = true,
                Attempts = 1,
                ElapsedMs = stopwatch.ElapsedMilliseconds
            };
        }

        return new CaptchaSolveResult
        {
            Status = CaptchaSolveStatus.InteractionDone,
            Provider = detected.Challenge.Provider,
            Kind = detected.Challenge.Kind,
            SolverId = SolverId(detected.Challenge.Kind),
            Actions = actions,
            Attempts = 1,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            VerificationEvidence = evidence
        };
    }

    private static async Task<string?> WaitForTurnstileAsync(
        IPage page,
        DetectedCaptcha detected,
        bool allowInteractiveClick,
        DateTimeOffset deadline,
        ICollection<CaptchaPlannedAction> actions,
        CancellationToken cancellationToken)
    {
        var clickAt = DateTimeOffset.UtcNow.Add(PassiveTurnstileWindow);
        var clickAttempted = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await HasInputValueAsync(
                    page,
                    detected.Primary,
                    "input[name='cf-turnstile-response']",
                    cancellationToken))
            {
                return "cf-turnstile-response foi produzido na sessão do navegador";
            }

            if (!clickAttempted && allowInteractiveClick && DateTimeOffset.UtcNow >= clickAt)
            {
                clickAttempted = true;
                if (await TryClickOnceAsync(page, detected, cancellationToken) is { } click)
                {
                    actions.Add(click);
                }
            }

            await DelayAsync(deadline, cancellationToken);
        }
        return null;
    }

    private static async Task<string?> WaitForFriendlyCaptchaAsync(
        IPage page,
        DetectedCaptcha detected,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        const string solutionSelector =
            "input[name='frc-captcha-solution'], input.frc-captcha-solution, " +
            "textarea[name='frc-captcha-solution']";
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await HasInputValueAsync(
                    page,
                    detected.Primary,
                    solutionSelector,
                    cancellationToken))
            {
                return "frc-captcha-solution foi produzido na sessão do navegador";
            }
            if (await IsFriendlySuccessAsync(detected.Primary, cancellationToken))
            {
                return "widget Friendly Captcha observou estado de sucesso";
            }
            await DelayAsync(deadline, cancellationToken);
        }
        return null;
    }

    private static async Task<string?> WaitForManagedChallengeAsync(
        IPage page,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detection = await CaptchaDetector.DetectAllAsync(
                page,
                executionId: null,
                actionId: null,
                cancellationToken);
            if (detection.Challenges.All(item =>
                    item.Challenge.Kind != CaptchaKind.CloudflareChallenge))
            {
                return "marcadores do Cloudflare Managed Challenge desapareceram";
            }
            await DelayAsync(deadline, cancellationToken);
        }
        return null;
    }

    private static async Task<CaptchaPlannedAction?> TryClickOnceAsync(
        IPage page,
        DetectedCaptcha detected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (detected.Primary is null ||
            detected.Challenge.ElementBounds is null)
        {
            return null;
        }

        try
        {
            if (!await detected.Primary.IsVisibleAsync())
            {
                return null;
            }
            var bounds = await detected.Primary.BoundingBoxAsync();
            if (bounds is null || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return null;
            }
            var x = bounds.X + bounds.Width / 2;
            var y = bounds.Y + bounds.Height / 2;
            await page.Mouse.ClickAsync(x, y);
            return new CaptchaPlannedAction(
                CaptchaActionKind.Click,
                TargetRole: "turnstile-widget",
                X: x,
                Y: y);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static async Task<bool> HasInputValueAsync(
        IPage page,
        ILocator? root,
        string selector,
        CancellationToken cancellationToken)
    {
        if (root is not null)
        {
            return await HasInputValueAsync(root.Locator(selector), cancellationToken);
        }
        foreach (var frame in page.Frames.ToArray())
        {
            if (await HasInputValueAsync(frame.Locator(selector), cancellationToken))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task<bool> HasInputValueAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        try
        {
            var count = Math.Min(await locator.CountAsync(), 16);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(await locator.Nth(index).InputValueAsync(
                        new LocatorInputValueOptions { Timeout = 500 })))
                {
                    return true;
                }
            }
        }
        catch (PlaywrightException)
        {
            return false;
        }
        return false;
    }

    private static async Task<bool> IsFriendlySuccessAsync(
        ILocator? root,
        CancellationToken cancellationToken)
    {
        if (root is null)
        {
            return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await root.EvaluateAsync<bool>(
                "element => element.classList.contains('frc-success') || " +
                "Boolean(element.querySelector('.frc-success'))");
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task DelayAsync(
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }
        await Task.Delay(remaining < PollInterval ? remaining : PollInterval, cancellationToken);
    }

    private static string SolverId(CaptchaKind kind) => kind switch
    {
        CaptchaKind.CloudflareTurnstile => "turnstile-same-page",
        CaptchaKind.FriendlyCaptcha => "friendlycaptcha-same-page",
        CaptchaKind.CloudflareChallenge => "cloudflare-managed-passive",
        _ => "same-page"
    };
}
