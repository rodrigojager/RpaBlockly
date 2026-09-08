using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Detecta uma lista de desafios com evidência e escopo. Ausência, desafio
/// desconhecido, widget oculto e widget já resolvido não compartilham estado.
/// </summary>
internal static class CaptchaDetector
{
    private const string ImageSelector =
        "img[id*='captcha' i], img[class*='captcha' i], img[alt*='captcha' i], " +
        "img[id*='codigo' i], img[alt*='código' i], img[src*='captcha' i], " +
        "canvas[id*='captcha' i], canvas[class*='captcha' i]";
    private const string ImageInputSelector =
        "input[id*='captcha' i], input[name*='captcha' i], " +
        "input[aria-label*='captcha' i], input[placeholder*='captcha' i], " +
        "input[id*='codigo' i], input[name*='codigo' i]";
    private const string SliderHandleSelector =
        "[class*='slider' i] [role='slider'], [class*='slider' i] button, " +
        "[class*='drag' i] [role='button'], [class*='slider-button' i], " +
        "[class*='slider-handle' i]";
    private const string SliderBackgroundSelector =
        "img[class*='puzzle' i], canvas[class*='puzzle' i], " +
        "img[class*='geetest' i], canvas[class*='geetest' i], " +
        "[class*='slider' i] img, [class*='slider' i] canvas";
    private const string SliderPieceSelector =
        "img[class*='piece' i], canvas[class*='piece' i], " +
        "img[class*='slice' i], canvas[class*='slice' i], " +
        "[class*='puzzle-piece' i]";
    private const string FriendlySelector =
        ".frc-captcha, [data-frc-captcha]";
    private const string GeeTestSelector =
        ".geetest_holder, .geetest_panel, [class^='geetest_'], [class*=' geetest_']";
    private const string AwsWafSelector =
        "#aws-waf-captcha-container, #awswaf-captcha, [id*='aws-waf-captcha' i], " +
        "[class*='aws-waf-captcha' i], " +
        "[data-testid*='aws-waf-captcha' i]";

    public enum Kind
    {
        None,
        Unknown,
        ImageText,
        RecaptchaV2,
        RecaptchaV3,
        RecaptchaEnterprise,
        Slider,
        HCaptcha,
        CloudflareTurnstile,
        CloudflareChallenge,
        ArkoseFunCaptcha,
        GeeTest,
        AwsWaf,
        FriendlyCaptcha
    }

    public enum DetectionStatus
    {
        NotPresent,
        Detected,
        Uncertain
    }

    public static async Task<Kind> DetectAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        var result = await DetectAllAsync(page, null, null, cancellationToken);
        if (result.Status == DetectionStatus.NotPresent)
        {
            return Kind.None;
        }

        return result.Challenges.Count == 1
            ? ToLegacyKind(result.Challenges[0].Challenge.Kind)
            : Kind.Unknown;
    }

    public static async Task<CaptchaDetectionResult> DetectAllAsync(
        IPage page,
        string? executionId,
        string? actionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var detections = new List<DetectedCaptcha>();
        var frames = page.Frames;
        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DetectInFrameAsync(
                frames[frameIndex],
                frameIndex,
                executionId,
                actionId,
                detections,
                cancellationToken);
        }

        if (detections.All(item =>
                item.Challenge.Kind != CaptchaKind.CloudflareTurnstile) &&
            await IsCloudflareChallengePageAsync(page, cancellationToken))
        {
            detections.Add(Create(
                executionId,
                actionId,
                "cloudflare",
                CaptchaKind.CloudflareChallenge,
                "managed-page",
                "main",
                visible: true,
                alreadySolved: false,
                "marcadores de challenge de página inteira"));
        }

        var distinct = detections
            .GroupBy(
                item => item.Challenge.SnapshotId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (distinct.Length == 0)
        {
            return new CaptchaDetectionResult(DetectionStatus.NotPresent, []);
        }

        var active = distinct
            .Where(item => item.Challenge.Visible && !item.Challenge.AlreadySolved)
            .ToArray();
        var status = active.Length > 0 && active.All(item => item.Challenge.Kind != CaptchaKind.Unknown)
            ? DetectionStatus.Detected
            : DetectionStatus.Uncertain;
        return new CaptchaDetectionResult(status, distinct);
    }

    private static async Task DetectInFrameAsync(
        IFrame frame,
        int frameIndex,
        string? executionId,
        string? actionId,
        ICollection<DetectedCaptcha> detections,
        CancellationToken cancellationToken)
    {
        var identity = FrameIdentity(frame, frameIndex);
        await DetectProviderIframesAsync(
            frame,
            identity,
            executionId,
            actionId,
            detections,
            cancellationToken);

        var imageMatch = await FindVisibleAsync(frame.Locator(ImageSelector), cancellationToken);
        var inputMatch = await FindVisibleAsync(frame.Locator(ImageInputSelector), cancellationToken);
        var image = imageMatch.Single;
        var input = inputMatch.Single;
        if (imageMatch.Count > 1 || inputMatch.Count > 1)
        {
            detections.Add(Create(
                executionId,
                actionId,
                "generic",
                CaptchaKind.Unknown,
                "ambiguous-image",
                identity,
                visible: true,
                alreadySolved: false,
                $"múltiplos candidatos de imagem/campo ({imageMatch.Count}/{inputMatch.Count})"));
        }
        else if (image is not null)
        {
            var bounds = await BoundsAsync(image, cancellationToken);
            detections.Add(Create(
                executionId,
                actionId,
                "generic",
                CaptchaKind.ImageText,
                input is null ? "image-only" : "image-input",
                identity,
                visible: true,
                alreadySolved: false,
                input is null
                    ? "imagem identificada sem campo de resposta único"
                    : "imagem e campo de resposta únicos",
                bounds,
                primary: input,
                secondary: image));
        }

        var handleMatch = await FindVisibleAsync(
            frame.Locator(SliderHandleSelector), cancellationToken);
        var backgroundMatch = await FindVisibleAsync(
            frame.Locator(SliderBackgroundSelector), cancellationToken);
        var pieceMatch = await FindVisibleAsync(
            frame.Locator(SliderPieceSelector), cancellationToken);
        var handle = handleMatch.Single;
        var background = backgroundMatch.Single;
        var piece = pieceMatch.Single;
        if (handleMatch.Count > 1 || backgroundMatch.Count > 1 || pieceMatch.Count > 1)
        {
            detections.Add(Create(
                executionId,
                actionId,
                "generic",
                CaptchaKind.Unknown,
                "ambiguous-slider",
                identity,
                visible: true,
                alreadySolved: false,
                $"múltiplos candidatos de slider " +
                $"({handleMatch.Count}/{backgroundMatch.Count}/{pieceMatch.Count})"));
        }
        else if (handle is not null || background is not null || piece is not null)
        {
            var complete = handle is not null && background is not null && piece is not null;
            var geetest = detections.FirstOrDefault(item =>
                item.Challenge.FrameIdentity == identity &&
                item.Challenge.Kind == CaptchaKind.GeeTest);
            if (complete || geetest is null)
            {
                if (complete && geetest is not null)
                {
                    detections.Remove(geetest);
                }
                detections.Add(Create(
                    executionId,
                    actionId,
                    geetest is null ? "generic" : "geetest",
                    complete ? CaptchaKind.Slider : CaptchaKind.Unknown,
                    complete
                        ? geetest is null ? "horizontal" : "geetest-horizontal"
                        : "incomplete-slider",
                    identity,
                    visible: true,
                    alreadySolved: false,
                    complete
                        ? "puxador, fundo e peça únicos"
                        : "marcadores de slider sem receita completa",
                    await BoundsAsync(background ?? handle ?? piece!, cancellationToken),
                    handle,
                    background,
                    piece));
            }
        }

        if (detections.All(item => item.Challenge.FrameIdentity != identity))
        {
            var generic = await SingleVisibleAsync(
                frame.Locator(
                    "[id*='captcha' i], [class*='captcha' i], " +
                    "[aria-label*='captcha' i]"),
                cancellationToken);
            if (generic is not null)
            {
                detections.Add(Create(
                    executionId,
                    actionId,
                    "unknown",
                    CaptchaKind.Unknown,
                    null,
                    identity,
                    visible: true,
                    alreadySolved: false,
                    "marcador genérico de captcha sem receita suportada",
                    await BoundsAsync(generic, cancellationToken)));
            }
        }
    }

    private static async Task DetectProviderIframesAsync(
        IFrame frame,
        string frameIdentity,
        string? executionId,
        string? actionId,
        ICollection<DetectedCaptcha> detections,
        CancellationToken cancellationToken)
    {
        var iframes = frame.Locator("iframe[src]");
        var count = Math.Min(await iframes.CountAsync(), 32);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var iframe = iframes.Nth(index);
            var source = await iframe.GetAttributeAsync("src") ?? string.Empty;
            var visible = await IsVisibleAsync(iframe, cancellationToken);
            var bounds = await BoundsAsync(iframe, cancellationToken);
            if (IsOfficialRecaptchaFrame(source, out var variant))
            {
                var solved = variant == "v2-checkbox" && await IsVisibleAsync(
                    iframe.ContentFrame.Locator(
                        "#recaptcha-anchor.recaptcha-checkbox-checked").First,
                    cancellationToken);
                detections.Add(Create(
                    executionId,
                    actionId,
                    "google",
                    variant == "enterprise"
                        ? CaptchaKind.RecaptchaEnterprise
                        : CaptchaKind.RecaptchaV2,
                    variant,
                    frameIdentity,
                    visible,
                    alreadySolved: solved,
                    "iframe com host oficial do reCAPTCHA",
                    bounds,
                    primary: iframe));
            }
            else if (IsOfficialHCaptchaFrame(source) &&
                !await HasAncestorAsync(iframe, ".h-captcha"))
            {
                detections.Add(Create(
                    executionId,
                    actionId,
                    "hcaptcha",
                    CaptchaKind.HCaptcha,
                    HCaptchaVariant(source),
                    frameIdentity,
                    visible,
                    alreadySolved: false,
                    "iframe com host oficial do hCaptcha",
                    bounds,
                    primary: iframe));
            }
            else if (IsOfficialTurnstileFrame(source) &&
                !await HasAncestorAsync(iframe, ".cf-turnstile"))
            {
                detections.Add(Create(
                    executionId,
                    actionId,
                    "cloudflare",
                    CaptchaKind.CloudflareTurnstile,
                    "widget",
                    frameIdentity,
                    visible,
                    alreadySolved: false,
                    "iframe com host oficial do Turnstile",
                    bounds,
                    primary: iframe));
            }
            else if (IsOfficialArkoseFrame(source))
            {
                detections.Add(Create(
                    executionId,
                    actionId,
                    "arkose",
                    CaptchaKind.ArkoseFunCaptcha,
                    "iframe",
                    frameIdentity,
                    visible,
                    alreadySolved: false,
                    "iframe com host oficial do Arkose Labs",
                    bounds,
                    primary: iframe));
            }
            else if (IsOfficialGeeTestFrame(source))
            {
                detections.Add(Create(
                    executionId,
                    actionId,
                    "geetest",
                    CaptchaKind.GeeTest,
                    "iframe",
                    frameIdentity,
                    visible,
                    alreadySolved: false,
                    "iframe com host oficial do GeeTest",
                    bounds,
                    primary: iframe));
            }
            else if (IsOfficialFriendlyFrame(source) &&
                !await HasAncestorAsync(iframe, ".frc-captcha, [data-frc-captcha]"))
            {
                detections.Add(Create(
                    executionId,
                    actionId,
                    "friendlycaptcha",
                    CaptchaKind.FriendlyCaptcha,
                    "iframe",
                    frameIdentity,
                    visible,
                    alreadySolved: false,
                    "iframe com host oficial do Friendly Captcha",
                    bounds,
                    primary: iframe));
            }
        }

        var turnstiles = frame.Locator(".cf-turnstile");
        var turnstileCount = Math.Min(await turnstiles.CountAsync(), 8);
        for (var index = 0; index < turnstileCount; index++)
        {
            var container = turnstiles.Nth(index);
            var visible = await IsVisibleAsync(container, cancellationToken);
            var hasToken = await HasValueAsync(
                container.Locator("input[name='cf-turnstile-response']").First,
                cancellationToken) || turnstileCount == 1 && await HasValueAsync(
                frame.Locator("input[name='cf-turnstile-response']").First,
                cancellationToken);
            detections.Add(Create(
                executionId,
                actionId,
                "cloudflare",
                CaptchaKind.CloudflareTurnstile,
                "widget",
                frameIdentity,
                visible,
                alreadySolved: false,
                hasToken
                    ? $"container .cf-turnstile[{index}] com token cliente"
                    : $"container .cf-turnstile[{index}]",
                await BoundsAsync(container, cancellationToken),
                primary: container));
        }

        var hcaptchas = frame.Locator(".h-captcha");
        var hcaptchaCount = Math.Min(await hcaptchas.CountAsync(), 8);
        for (var index = 0; index < hcaptchaCount; index++)
        {
            var container = hcaptchas.Nth(index);
            var visible = await IsVisibleAsync(container, cancellationToken);
            var hasToken = await HasValueAsync(
                container.Locator("textarea[name='h-captcha-response']").First,
                cancellationToken) || hcaptchaCount == 1 && await HasValueAsync(
                frame.Locator("textarea[name='h-captcha-response']").First,
                cancellationToken);
            detections.Add(Create(
                executionId,
                actionId,
                "hcaptcha",
                CaptchaKind.HCaptcha,
                "widget",
                frameIdentity,
                visible,
                alreadySolved: false,
                hasToken
                    ? $"container .h-captcha[{index}] com token cliente"
                    : $"container .h-captcha[{index}]",
                await BoundsAsync(container, cancellationToken),
                primary: container));
        }

        var friendlyWidgets = frame.Locator(FriendlySelector);
        var friendlyCount = Math.Min(await friendlyWidgets.CountAsync(), 8);
        for (var index = 0; index < friendlyCount; index++)
        {
            var widget = friendlyWidgets.Nth(index);
            var visible = await IsVisibleAsync(widget, cancellationToken);
            detections.Add(Create(
                executionId,
                actionId,
                "friendlycaptcha",
                CaptchaKind.FriendlyCaptcha,
                "widget",
                frameIdentity,
                visible,
                alreadySolved: false,
                $"widget Friendly Captcha[{index}]",
                await BoundsAsync(widget, cancellationToken),
                primary: widget));
        }

        if (detections.All(item =>
                item.Challenge.FrameIdentity != frameIdentity ||
                item.Challenge.Kind != CaptchaKind.GeeTest))
        {
            var geetest = await SingleVisibleAsync(
                frame.Locator(GeeTestSelector), cancellationToken);
            var geetestScript = await frame.Locator(
                "script[src*='geetest.com' i], script[src*='geevisit.com' i]")
                .CountAsync() > 0;
            if (geetest is not null || geetestScript)
            {
                detections.Add(Create(
                    executionId,
                    actionId,
                    "geetest",
                    CaptchaKind.GeeTest,
                    geetest is null ? "script" : "widget",
                    frameIdentity,
                    visible: geetest is not null,
                    alreadySolved: false,
                    geetest is null ? "script oficial do GeeTest" : "container GeeTest",
                    geetest is null ? null : await BoundsAsync(geetest, cancellationToken),
                    primary: geetest));
            }
        }

        var awsWaf = await SingleVisibleAsync(
            frame.Locator(AwsWafSelector), cancellationToken);
        var hasAwsWafApi = await EvaluateBooleanAsync(
            frame,
            "() => typeof window.AwsWafCaptcha === 'object' || " +
                "typeof window.AwsWafIntegration === 'object'",
            cancellationToken);
        if (awsWaf is not null || hasAwsWafApi)
        {
            detections.Add(Create(
                executionId,
                actionId,
                "aws",
                CaptchaKind.AwsWaf,
                awsWaf is null ? "javascript-challenge" : "interactive",
                frameIdentity,
                visible: awsWaf is not null,
                alreadySolved: false,
                awsWaf is null ? "API JavaScript do AWS WAF" : "container AWS WAF CAPTCHA",
                awsWaf is null ? null : await BoundsAsync(awsWaf, cancellationToken),
                primary: awsWaf));
        }

        var enterprise = await frame.Locator(
            "script[src*='/recaptcha/enterprise.js' i], " +
            "script[src*='/recaptcha/enterprise/' i]").CountAsync() > 0;
        var v3 = await frame.Locator(
            "script[src*='/recaptcha/api.js?render=' i]").CountAsync() > 0;
        var invisibleV2 = await SingleVisibleAsync(
            frame.Locator(".g-recaptcha[data-size='invisible']"), cancellationToken);
        if (invisibleV2 is not null && detections.All(item =>
                item.Challenge.FrameIdentity != frameIdentity ||
                item.Challenge.Kind != CaptchaKind.RecaptchaV2))
        {
            detections.Add(Create(
                executionId,
                actionId,
                "google",
                CaptchaKind.RecaptchaV2,
                "v2-invisible",
                frameIdentity,
                visible: true,
                alreadySolved: false,
                "widget reCAPTCHA v2 invisível",
                await BoundsAsync(invisibleV2, cancellationToken),
                primary: invisibleV2));
        }
        var scriptedKind = enterprise
            ? CaptchaKind.RecaptchaEnterprise
            : CaptchaKind.RecaptchaV3;
        if ((enterprise || v3) && detections.All(item =>
                item.Challenge.FrameIdentity != frameIdentity ||
                item.Challenge.Kind != scriptedKind))
        {
            detections.Add(Create(
                executionId,
                actionId,
                "google",
                scriptedKind,
                enterprise ? "enterprise-script" : "score-or-invisible",
                frameIdentity,
                visible: false,
                alreadySolved: false,
                enterprise
                    ? "script do reCAPTCHA Enterprise"
                    : "script render/invisible do reCAPTCHA"));
        }
    }

    private static DetectedCaptcha Create(
        string? executionId,
        string? actionId,
        string provider,
        CaptchaKind kind,
        string? variant,
        string frameIdentity,
        bool visible,
        bool alreadySolved,
        string evidence,
        CaptchaElementBounds? bounds = null,
        ILocator? primary = null,
        ILocator? secondary = null,
        ILocator? tertiary = null)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var snapshotSeed = $"{provider}|{kind}|{variant}|{frameIdentity}|" +
            $"{bounds?.X}|{bounds?.Y}|{bounds?.Width}|{bounds?.Height}|{evidence}";
        var snapshotId = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(snapshotSeed)));
        var challengeId = $"captcha-{snapshotId[..16].ToLowerInvariant()}";
        return new DetectedCaptcha(
            new CaptchaChallenge(
                challengeId,
                executionId,
                actionId,
                provider,
                kind,
                variant,
                frameIdentity,
                snapshotId,
                capturedAt,
                bounds,
                RecipeId: null,
                visible,
                alreadySolved,
                evidence),
            primary,
            secondary,
            tertiary);
    }

    private static async Task<bool> IsCloudflareChallengePageAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = page.Url;
        if (url.Contains("/cdn-cgi/challenge-platform/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (page.Frames.Any(frame =>
                frame.Url.Contains(
                    "/cdn-cgi/challenge-platform/", StringComparison.OrdinalIgnoreCase) &&
                !frame.Url.Contains("turnstile", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        if (await page.Locator(
                "#challenge-running, #challenge-stage, form#challenge-form, " +
                "[data-translate='checking_browser'], [data-translate='challenge_headline']")
                .CountAsync() > 0)
        {
            return true;
        }
        return false;
    }

    private static bool IsOfficialRecaptchaFrame(string source, out string variant)
    {
        variant = "v2";
        if (!TryOfficialUri(source, ["www.google.com", "www.recaptcha.net"], out var uri) ||
            !uri.AbsolutePath.Contains("/recaptcha/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (uri.AbsolutePath.Contains("/enterprise/", StringComparison.OrdinalIgnoreCase))
        {
            variant = "enterprise";
        }
        else if (uri.AbsolutePath.Contains("anchor", StringComparison.OrdinalIgnoreCase))
        {
            variant = "v2-checkbox";
        }
        else if (uri.AbsolutePath.Contains("bframe", StringComparison.OrdinalIgnoreCase))
        {
            variant = "v2-challenge";
        }
        return true;
    }

    private static bool IsOfficialHCaptchaFrame(string source) =>
        TryOfficialUri(source, ["hcaptcha.com", "newassets.hcaptcha.com"], out _);

    private static string HCaptchaVariant(string source) =>
        source.Contains("frame=challenge", StringComparison.OrdinalIgnoreCase) ||
        source.Contains("/challenge", StringComparison.OrdinalIgnoreCase)
            ? "challenge"
            : "checkbox";

    private static bool IsOfficialTurnstileFrame(string source) =>
        TryOfficialUri(source, ["challenges.cloudflare.com"], out var uri) &&
        uri.AbsolutePath.Contains("turnstile", StringComparison.OrdinalIgnoreCase);

    private static bool IsOfficialArkoseFrame(string source) =>
        TryOfficialUriSuffix(
            source,
            ["arkoselabs.com", "funcaptcha.com"],
            out var uri) &&
        (uri.AbsolutePath.Contains("/fc/", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Contains("client-api", StringComparison.OrdinalIgnoreCase));

    private static bool IsOfficialGeeTestFrame(string source) =>
        TryOfficialUriSuffix(source, ["geetest.com", "geevisit.com"], out _);

    private static bool IsOfficialFriendlyFrame(string source) =>
        TryOfficialUriSuffix(
            source,
            ["friendlycaptcha.com", "friendlycaptcha.eu", "frcapi.com"],
            out _);

    private static bool TryOfficialUri(
        string value,
        IReadOnlyCollection<string> hosts,
        out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps &&
            parsed.IsDefaultPort &&
            hosts.Any(host => parsed.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool TryOfficialUriSuffix(
        string value,
        IReadOnlyCollection<string> hostSuffixes,
        out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps &&
            parsed.IsDefaultPort &&
            hostSuffixes.Any(suffix =>
                parsed.Host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                parsed.Host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase)))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static async Task<bool> HasAncestorAsync(
        ILocator locator,
        string selector)
    {
        try
        {
            return await locator.EvaluateAsync<bool>(
                "(element, selector) => Boolean(element.closest(selector))",
                selector);
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task<bool> EvaluateBooleanAsync(
        IFrame frame,
        string script,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await frame.EvaluateAsync<bool>(script);
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task<ILocator?> SingleVisibleAsync(
        ILocator locator,
        CancellationToken cancellationToken) =>
        (await FindVisibleAsync(locator, cancellationToken)).Single;

    private static async Task<VisibleLocatorResult> FindVisibleAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        var count = Math.Min(await locator.CountAsync(), 16);
        ILocator? found = null;
        var visibleCount = 0;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = locator.Nth(index);
            if (!await IsVisibleAsync(candidate, cancellationToken))
            {
                continue;
            }
            visibleCount++;
            found ??= candidate;
        }
        return new VisibleLocatorResult(visibleCount == 1 ? found : null, visibleCount);
    }

    private static async Task<bool> IsVisibleAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await locator.IsVisibleAsync();
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task<bool> HasValueAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return !string.IsNullOrWhiteSpace(await locator.InputValueAsync(
                new LocatorInputValueOptions { Timeout = 500 }));
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task<CaptchaElementBounds?> BoundsAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var box = await locator.BoundingBoxAsync();
            return box is null
                ? null
                : new CaptchaElementBounds(box.X, box.Y, box.Width, box.Height);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static string FrameIdentity(IFrame frame, int index)
    {
        if (index == 0)
        {
            return "main";
        }
        if (Uri.TryCreate(frame.Url, UriKind.Absolute, out var uri))
        {
            return $"frame-{index}:{uri.Host}";
        }
        return $"frame-{index}";
    }

    private static Kind ToLegacyKind(CaptchaKind kind) => kind switch
    {
        CaptchaKind.ImageText => Kind.ImageText,
        CaptchaKind.RecaptchaV2 => Kind.RecaptchaV2,
        CaptchaKind.RecaptchaV3 => Kind.RecaptchaV3,
        CaptchaKind.RecaptchaEnterprise => Kind.RecaptchaEnterprise,
        CaptchaKind.Slider => Kind.Slider,
        CaptchaKind.HCaptcha => Kind.HCaptcha,
        CaptchaKind.CloudflareTurnstile => Kind.CloudflareTurnstile,
        CaptchaKind.CloudflareChallenge => Kind.CloudflareChallenge,
        CaptchaKind.ArkoseFunCaptcha => Kind.ArkoseFunCaptcha,
        CaptchaKind.GeeTest => Kind.GeeTest,
        CaptchaKind.AwsWaf => Kind.AwsWaf,
        CaptchaKind.FriendlyCaptcha => Kind.FriendlyCaptcha,
        _ => Kind.Unknown
    };

    private sealed record VisibleLocatorResult(ILocator? Single, int Count);
}

internal sealed record CaptchaDetectionResult(
    CaptchaDetector.DetectionStatus Status,
    IReadOnlyList<DetectedCaptcha> Challenges);

internal sealed record DetectedCaptcha(
    CaptchaChallenge Challenge,
    ILocator? Primary = null,
    ILocator? Secondary = null,
    ILocator? Tertiary = null);
