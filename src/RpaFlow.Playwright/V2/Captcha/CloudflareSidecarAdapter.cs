using Microsoft.Playwright;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Faz transferência experimental de cf_clearance e prova seu efeito somente
/// recarregando e reobservando a sessão Playwright original.
/// </summary>
internal static class CloudflareSidecarAdapter
{
    public static async Task<CaptchaSolveResult> ExecuteAsync(
        IPage page,
        DetectedCaptcha detected,
        CaptchaOptions options,
        CancellationToken cancellationToken)
    {
        if (detected.Challenge.Kind != CaptchaKind.CloudflareChallenge)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.UnsupportedType,
                "O sidecar é restrito a Cloudflare Managed Challenge.",
                attempts: 0);
        }
        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var target))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "A página atual não possui URL absoluta para o sidecar.",
                attempts: 0);
        }

        var userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
        using var client = new CloudflareSidecarClient(options);
        var solution = await client.SolveAsync(target, userAgent, cancellationToken);
        await page.Context.AddCookiesAsync(solution.Cookies);
        await page.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = options.CloudflareSidecarTimeoutSeconds * 1000
        });
        var after = await CaptchaDetector.DetectAllAsync(
            page,
            detected.Challenge.ExecutionId,
            detected.Challenge.ActionId,
            cancellationToken);
        var remains = after.Challenges.Any(item =>
            item.Challenge.Kind == CaptchaKind.CloudflareChallenge &&
            item.Challenge.Visible &&
            !item.Challenge.AlreadySolved);

        if (remains)
        {
            return new CaptchaSolveResult
            {
                Status = CaptchaSolveStatus.NeedsHuman,
                Provider = "cloudflare",
                Kind = CaptchaKind.CloudflareChallenge,
                SolverId = solution.Provider + "-artifact-transfer",
                ModelVersion = solution.Version,
                ErrorCode = CaptchaErrorCodes.VerificationFailed,
                ErrorMessage =
                    "O clearance foi aplicado, mas o Managed Challenge continuou visível.",
                Retryable = false,
                Attempts = 1,
                ElapsedMs = solution.ElapsedMs
            };
        }

        return new CaptchaSolveResult
        {
            Status = CaptchaSolveStatus.InteractionDone,
            Provider = "cloudflare",
            Kind = CaptchaKind.CloudflareChallenge,
            SolverId = solution.Provider + "-artifact-transfer",
            ModelVersion = solution.Version,
            Attempts = 1,
            ElapsedMs = solution.ElapsedMs,
            VerificationEvidence =
                "cf_clearance filtrado foi aplicado e os marcadores do Managed Challenge " +
                $"desapareceram na sessão original; User-Agent compatível: {solution.UserAgentMatched}"
        };
    }
}
