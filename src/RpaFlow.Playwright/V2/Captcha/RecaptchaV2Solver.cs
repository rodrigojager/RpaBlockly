using System.Security.Cryptography;
using Microsoft.Playwright;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Resolve reCAPTCHA v2 (checkbox) via desafio de áudio: clica no checkbox,
/// troca para o desafio de áudio, baixa o MP3, envia ao serviço Python de
/// transcrição (faster-whisper) e preenche. Repete até RecaptchaMaxAttempts.
/// </summary>
internal static class RecaptchaV2Solver
{
    private const int MaximumAudioBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AudioHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "www.google.com",
            "www.recaptcha.net"
        };

    public static async Task<RecaptchaV2SolveOutcome> ExecuteAsync(
        IPage page,
        CaptchaOptions options,
        int? maximumAttempts,
        Action<int> observeAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var anchorFrame = page.FrameLocator("iframe[src*='recaptcha'][src*='anchor']");
        var checkbox = anchorFrame.Locator("#recaptcha-anchor");
        await checkbox.First.ClickAsync(
            new LocatorClickOptions { Delay = 60 });

        if (await IsSolvedAsync(anchorFrame, cancellationToken))
        {
            return new RecaptchaV2SolveOutcome(true, 0);
        }

        using var client = new CaptchaServiceClient(options);
        using var downloadHandler = new HttpClientHandler { AllowAutoRedirect = false };
        using var downloadClient = new HttpClient(downloadHandler)
        {
            Timeout = TimeSpan.FromSeconds(options.ServiceTimeoutSeconds)
        };
        var maximum = ResolveMaximumAttempts(options, maximumAttempts);
        var attemptsUsed = 0;
        var deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(options.DeadlineSeconds);
        while (attemptsUsed < maximum)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var challengeFrame = page.FrameLocator("iframe[src*='recaptcha'][src*='bframe']");

            if (attemptsUsed == 0)
            {
                await challengeFrame
                    .Locator("#recaptcha-audio-button")
                    .ClickAsync(new LocatorClickOptions { Delay = 60 });
            }
            else
            {
                await challengeFrame
                    .Locator(".rc-button-default")
                    .First.ClickAsync(new LocatorClickOptions { Delay = 40 });
            }

            var audioLink = challengeFrame.Locator("a.rc-audiochallenge-tdk");
            var audioUrl = await audioLink.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(audioUrl))
            {
                throw new InvalidOperationException(
                    "O desafio de áudio do reCAPTCHA não está disponível.");
            }

            var audioBytes = await DownloadAudioAsync(
                downloadClient,
                RequireOfficialAudioUri(audioUrl),
                cancellationToken);
            var snapshotId = Convert.ToHexString(SHA256.HashData(audioBytes));
            CaptchaSolveResult inference;
            try
            {
                inference = await client.SolveV2Async(
                    new CaptchaServiceSolveRequest(
                        Guid.NewGuid().ToString("N"),
                        $"recaptcha-v2-{snapshotId[..16].ToLowerInvariant()}",
                        snapshotId,
                        "recaptcha_v2_audio",
                        new Dictionary<string, object?>
                        {
                            ["audioBase64"] = Convert.ToBase64String(audioBytes)
                        },
                        new Dictionary<string, object?>(),
                        Hint: null,
                        deadlineUtc,
                        new Dictionary<string, object?>
                        {
                            ["maxAttempts"] = maximum - attemptsUsed,
                            ["language"] = "en",
                            ["localOnly"] = true,
                            ["allowVlmFallback"] = false
                        },
                        Provider: "google",
                        Kind: CaptchaKind.RecaptchaV2),
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
            var transcript = inference.Answer;
            if (string.IsNullOrWhiteSpace(transcript))
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.ContractViolation,
                    "O serviço V2 não produziu transcrição para o desafio de áudio.",
                    attempts: attemptsUsed);
            }

            await challengeFrame
                .Locator("#audio-response")
                .FillAsync(transcript, new LocatorFillOptions());
            await challengeFrame
                .Locator("#recaptcha-verify-button")
                .ClickAsync(new LocatorClickOptions { Delay = 80 });

            if (await IsSolvedAsync(anchorFrame, cancellationToken))
            {
                return new RecaptchaV2SolveOutcome(true, attemptsUsed);
            }

            // novo desafio ("várias soluções corretas") segue o loop
        }

        return new RecaptchaV2SolveOutcome(false, attemptsUsed);
    }

    internal static int ResolveMaximumAttempts(
        CaptchaOptions options,
        int? actionMaximumAttempts) =>
        Math.Clamp(actionMaximumAttempts ?? options.RecaptchaMaxAttempts, 1, 10);

    internal static Uri RequireOfficialAudioUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort ||
            !AudioHosts.Contains(uri.Host) ||
            !uri.AbsolutePath.StartsWith("/recaptcha/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "O desafio de áudio apontou para uma URL não permitida.");
        }

        return uri;
    }

    private static async Task<byte[]> DownloadAudioAsync(
        HttpClient client,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new InvalidOperationException(
                "O download do desafio de áudio tentou redirecionar para outra URL.");
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumAudioBytes)
        {
            throw new InvalidOperationException(
                "O desafio de áudio excedeu o limite de 5 MiB.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81_920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > MaximumAudioBytes)
            {
                throw new InvalidOperationException(
                    "O desafio de áudio excedeu o limite de 5 MiB.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static async Task<bool> IsSolvedAsync(
        IFrameLocator anchorFrame,
        CancellationToken cancellationToken)
    {
        try
        {
            await anchorFrame
                .Locator("#recaptcha-anchor.recaptcha-checkbox-checked")
                .WaitForAsync(
                    new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Attached,
                        Timeout = 4_000
                    });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}

internal sealed record RecaptchaV2SolveOutcome(bool Solved, int Attempts);
