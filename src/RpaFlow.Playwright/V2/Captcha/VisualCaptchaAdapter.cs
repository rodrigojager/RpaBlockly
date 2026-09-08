using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Playwright;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Captura o desafio para inferência e executa somente ações tipadas, após
/// confirmar que imagem, elemento e geometria continuam correlacionados ao
/// snapshot enviado ao serviço.
/// </summary>
internal static class VisualCaptchaAdapter
{
    public static async Task<CaptchaSolveResult> ExecuteAsync(
        IPage page,
        DetectedCaptcha detected,
        CaptchaOptions options,
        bool localOnly,
        int maxAttempts,
        string? hint,
        Action<int> observeAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(detected);
        ArgumentNullException.ThrowIfNull(options);
        var primary = detected.Primary ?? throw new CaptchaException(
            CaptchaErrorCodes.NeedsConfiguration,
            "O desafio visual não possui um locator único para captura.");
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.UpstreamUnavailable,
                "O fallback visual exige Captcha.ServiceUrl.",
                retryable: true);
        }

        var screenshot = await CaptureAsync(primary, cancellationToken);
        var (width, height) = ReadPngDimensions(screenshot, options.MaximumImagePixels);
        var initialBounds = await primary.BoundingBoxAsync() ?? throw new CaptchaException(
            CaptchaErrorCodes.ExtractionFailed,
            "O desafio visual não possui geometria capturável.");
        var initialElement = await primary.ElementHandleAsync() ?? throw StaleSnapshot();
        ValidateBounds(initialBounds);
        var snapshotId = Hash(screenshot);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.DeadlineSeconds);
        using var client = new CaptchaServiceClient(options);
        CaptchaSolveResult inference;
        try
        {
            inference = await client.SolveV2Async(
                new CaptchaServiceSolveRequest(
                    Guid.NewGuid().ToString("N"),
                    detected.Challenge.ChallengeId,
                    snapshotId,
                    "visual",
                    new Dictionary<string, object?>
                    {
                        ["imageBase64"] = Convert.ToBase64String(screenshot)
                    },
                    new Dictionary<string, object?>
                    {
                        ["width"] = width,
                        ["height"] = height,
                        ["cssX"] = initialBounds.X,
                        ["cssY"] = initialBounds.Y,
                        ["cssWidth"] = initialBounds.Width,
                        ["cssHeight"] = initialBounds.Height
                    },
                    hint,
                    deadline,
                    new Dictionary<string, object?>
                    {
                        ["maxAttempts"] = Math.Clamp(maxAttempts, 1, 10),
                        ["localOnly"] = localOnly,
                        ["allowVlmFallback"] = true
                    },
                    detected.Challenge.Provider,
                    detected.Challenge.Variant,
                    TaskFor(detected.Challenge.Kind),
                    detected.Challenge.Kind),
                cancellationToken);
            observeAttempts(inference.Attempts);
        }
        catch (CaptchaException exception)
        {
            observeAttempts(exception.Attempts);
            throw;
        }

        await ValidateRetainedSnapshotAsync(
            primary,
            initialElement,
            initialBounds,
            screenshot,
            cancellationToken);

        var executed = await ExecuteActionsAsync(
            page,
            inference.Actions,
            initialBounds,
            width,
            height,
            cancellationToken,
            token => ValidateRetainedElementAsync(
                initialElement,
                initialBounds,
                token),
            token => ValidateRetainedSnapshotAsync(
                primary,
                initialElement,
                initialBounds,
                screenshot,
                token));
        return inference with
        {
            Status = CaptchaSolveStatus.InteractionDone,
            Actions = executed,
            VerificationEvidence =
                "ações VLM executadas sobre o snapshot correlacionado; aceite ainda não presumido"
        };
    }

    internal static async Task<IReadOnlyList<CaptchaPlannedAction>> ExecuteActionsAsync(
        IPage page,
        IReadOnlyList<CaptchaPlannedAction> actions,
        LocatorBoundingBoxResult bounds,
        int imageWidth,
        int imageHeight,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? validateSnapshot = null,
        Func<CancellationToken, Task>? validateInitialSnapshot = null)
    {
        ValidateBounds(bounds);
        if (imageWidth <= 0 || imageHeight <= 0 || actions.Count is < 1 or > 100)
        {
            throw ContractViolation("A captura ou a quantidade de ações VLM é inválida.");
        }

        var starts = new (float X, float Y)[actions.Count];
        var ends = new (float X, float Y)?[actions.Count];
        for (var index = 0; index < actions.Count; index++)
        {
            if (actions[index].Kind is not (CaptchaActionKind.Click or CaptchaActionKind.Drag))
            {
                throw ContractViolation(
                    $"Ação VLM '{actions[index].Kind}' não é permitida para desafio visual.");
            }
            starts[index] = MapPoint(
                actions[index].X,
                actions[index].Y,
                bounds,
                imageWidth,
                imageHeight);
            if (actions[index].Kind == CaptchaActionKind.Drag)
            {
                ends[index] = MapPoint(
                    actions[index].ToX,
                    actions[index].ToY,
                    bounds,
                    imageWidth,
                    imageHeight);
            }
        }

        var targetSnapshots = new byte[actions.Count][];
        var endSnapshots = new byte[actions.Count][];
        for (var index = 1; index < actions.Count; index++)
        {
            targetSnapshots[index] = await CapturePointAsync(
                page,
                starts[index].X,
                starts[index].Y,
                cancellationToken);
            if (ends[index] is { } end)
            {
                endSnapshots[index] = await CapturePointAsync(
                    page,
                    end.X,
                    end.Y,
                    cancellationToken);
            }
        }
        if (validateInitialSnapshot is not null)
        {
            await validateInitialSnapshot(cancellationToken);
        }

        var executed = new List<CaptchaPlannedAction>(actions.Count);
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            cancellationToken.ThrowIfCancellationRequested();
            if (executed.Count > 0 && validateSnapshot is not null)
            {
                await validateSnapshot(cancellationToken);
            }
            var (fromX, fromY) = starts[index];
            if (index > 0)
            {
                var currentTarget = await CapturePointAsync(
                    page,
                    fromX,
                    fromY,
                    cancellationToken);
                if (!Hash(currentTarget).Equals(
                        Hash(targetSnapshots[index]),
                        StringComparison.Ordinal))
                {
                    throw StaleSnapshot();
                }
                if (ends[index] is { } end)
                {
                    var currentEnd = await CapturePointAsync(
                        page,
                        end.X,
                        end.Y,
                        cancellationToken);
                    if (!Hash(currentEnd).Equals(
                            Hash(endSnapshots[index]),
                            StringComparison.Ordinal))
                    {
                        throw StaleSnapshot();
                    }
                }
            }
            if (action.Kind == CaptchaActionKind.Click)
            {
                await page.Mouse.ClickAsync(fromX, fromY);
                executed.Add(action with { X = fromX, Y = fromY });
                continue;
            }

            var (toX, toY) = ends[index]!.Value;
            await HumanizedDrag.ExecuteAsync(
                page,
                fromX,
                fromY,
                toX,
                toY,
                cancellationToken);
            executed.Add(action with
            {
                X = fromX,
                Y = fromY,
                ToX = toX,
                ToY = toY
            });
        }
        return executed;
    }

    private static async Task<byte[]> CapturePointAsync(
        IPage page,
        float x,
        float y,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const float size = 9;
        var viewport = page.ViewportSize;
        var left = Math.Max(0, MathF.Floor(x - size / 2));
        var top = Math.Max(0, MathF.Floor(y - size / 2));
        var width = viewport is null ? 1 : Math.Min(size, viewport.Width - left);
        var height = viewport is null ? 1 : Math.Min(size, viewport.Height - top);
        if (width <= 0 || height <= 0)
        {
            throw StaleSnapshot();
        }
        return await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            Clip = new Clip
            {
                X = left,
                Y = top,
                Width = width,
                Height = height
            }
        });
    }

    internal static async Task ValidateRetainedElementAsync(
        IElementHandle initialElement,
        LocatorBoundingBoxResult initialBounds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentBounds = await initialElement.BoundingBoxAsync();
        if (currentBounds is null ||
            Math.Abs(initialBounds.X - currentBounds.X) > 1 ||
            Math.Abs(initialBounds.Y - currentBounds.Y) > 1 ||
            Math.Abs(initialBounds.Width - currentBounds.Width) > 1 ||
            Math.Abs(initialBounds.Height - currentBounds.Height) > 1)
        {
            throw StaleSnapshot();
        }
    }

    internal static async Task ValidateRetainedSnapshotAsync(
        ILocator locator,
        IElementHandle initialElement,
        LocatorBoundingBoxResult initialBounds,
        byte[] initialScreenshot,
        CancellationToken cancellationToken)
    {
        var current = await CaptureAsync(locator, cancellationToken);
        var currentBounds = await locator.BoundingBoxAsync() ?? throw StaleSnapshot();
        if (!Hash(current).Equals(Hash(initialScreenshot), StringComparison.Ordinal) ||
            !SameBounds(initialBounds, currentBounds))
        {
            throw StaleSnapshot();
        }
        await ValidateRetainedElementAsync(initialElement, initialBounds, cancellationToken);
    }

    private static async Task<byte[]> CaptureAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await locator.ScreenshotAsync(
                new LocatorScreenshotOptions { Type = ScreenshotType.Png });
        }
        catch (PlaywrightException exception)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ExtractionFailed,
                "Não foi possível capturar o desafio visual.",
                retryable: true,
                innerException: exception);
        }
    }

    private static (int Width, int Height) ReadPngDimensions(
        byte[] image,
        int maximumPixels)
    {
        if (image.Length < 24 ||
            !image.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            throw ContractViolation("A captura visual não é um PNG válido.");
        }
        var width = BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(20, 4));
        if (width <= 0 || height <= 0 ||
            (long)width * height > maximumPixels)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "A captura visual excede o limite de pixels.");
        }
        return (width, height);
    }

    private static (float X, float Y) MapPoint(
        float? x,
        float? y,
        LocatorBoundingBoxResult bounds,
        int imageWidth,
        int imageHeight)
    {
        if (x is null || y is null ||
            !float.IsFinite(x.Value) || !float.IsFinite(y.Value) ||
            x < 0 || y < 0 || x >= imageWidth || y >= imageHeight)
        {
            throw ContractViolation("O VLM produziu coordenadas fora da captura.");
        }
        return (
            bounds.X + x.Value / imageWidth * bounds.Width,
            bounds.Y + y.Value / imageHeight * bounds.Height);
    }

    private static void ValidateBounds(LocatorBoundingBoxResult bounds)
    {
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) ||
            !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) ||
            bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ExtractionFailed,
                "A geometria do desafio visual é inválida.");
        }
    }

    private static bool SameBounds(
        LocatorBoundingBoxResult expected,
        LocatorBoundingBoxResult current) =>
        Math.Abs(expected.X - current.X) <= 1 &&
        Math.Abs(expected.Y - current.Y) <= 1 &&
        Math.Abs(expected.Width - current.Width) <= 1 &&
        Math.Abs(expected.Height - current.Height) <= 1;

    private static string TaskFor(CaptchaKind kind) => kind switch
    {
        CaptchaKind.HCaptcha or CaptchaKind.RecaptchaEnterprise or CaptchaKind.AwsWaf =>
            "grid",
        CaptchaKind.ArkoseFunCaptcha => "rotation",
        CaptchaKind.GeeTest => "drag",
        _ => throw new CaptchaException(
            CaptchaErrorCodes.UnsupportedType,
            $"Não há tarefa visual configurada para {kind}.")
    };

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static CaptchaException StaleSnapshot() =>
        new(
            CaptchaErrorCodes.StaleSnapshot,
            "O elemento do desafio visual mudou; as ações restantes foram interrompidas.",
            retryable: true);

    private static CaptchaException ContractViolation(string message) =>
        new(CaptchaErrorCodes.ContractViolation, message);
}
