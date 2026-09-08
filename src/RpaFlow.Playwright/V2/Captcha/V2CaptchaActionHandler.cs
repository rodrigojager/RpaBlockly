using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using RpaFlow.Contracts.V2;
using RpaFlow.Runtime.V2;
using FlowActionDefinition = RpaFlow.Contracts.V2.FlowActionDefinition;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Handler V2 das ações de resolução de captcha: OCR de imagem embutido,
/// reCAPTCHA v2 por áudio (serviço externo), slider e o fallback humano.
/// </summary>
internal sealed class V2CaptchaActionHandler : IV2FlowActionHandler
{
    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "solveImageCaptcha",
            "solveRecaptchaV2",
            "solveSliderCaptcha",
            "solveHCaptcha",
            "solveCaptcha",
            "waitHumanInput"
        };

    public async Task ExecuteAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var options = execution.Context.Options.Captcha ?? new CaptchaOptions();
        var observedAttempts = 0;
        void ObserveAttempts(int value) =>
            observedAttempts = Math.Max(observedAttempts, Math.Clamp(value, 0, 10));
        var budget = TimeSpan.FromSeconds(options.DeadlineSeconds);
        if (action.TimeoutMs is { } timeoutMs && timeoutMs > 0)
        {
            budget = TimeSpan.FromMilliseconds(Math.Min(budget.TotalMilliseconds, timeoutMs));
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!action.Type.Equals("waitHumanInput", StringComparison.OrdinalIgnoreCase))
        {
            deadline.CancelAfter(budget);
        }
        try
        {
            switch (action.Type.ToLowerInvariant())
            {
                case "solveimagecaptcha":
                    await SolveImageCaptchaAsync(
                        action, execution, ObserveAttempts, deadline.Token);
                    return;
                case "solverecaptchav2":
                    await SolveRecaptchaAsync(
                        action, execution, ObserveAttempts, deadline.Token);
                    return;
                case "solvehcaptcha":
                    await SolveHCaptchaAsync(
                        action, execution, ObserveAttempts, deadline.Token);
                    return;
                case "solveslidercaptcha":
                    await SolveSliderAsync(
                        action, execution, ObserveAttempts, deadline.Token);
                    return;
                case "solvecaptcha":
                    await SolveAutoAsync(
                        action,
                        execution,
                        ObserveAttempts,
                        deadline.Token,
                        cancellationToken);
                    return;
                case "waithumaninput":
                    await WaitHumanInputAsync(action, execution, cancellationToken);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"O handler V2 de captcha não interpreta '{action.Type}'.");
            }
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            if (action.Optional)
            {
                WriteResult(action, execution, new CaptchaSolveResult
                {
                    Status = CaptchaSolveStatus.Expired,
                    Kind = KindForAction(action),
                    SolverId = "captcha-deadline",
                    ErrorCode = CaptchaErrorCodes.DeadlineExceeded,
                    ErrorMessage = "A ação de captcha ultrapassou o prazo total configurado.",
                    Retryable = true,
                    Attempts = observedAttempts
                });
                return;
            }
            throw new CaptchaException(
                CaptchaErrorCodes.DeadlineExceeded,
                "A ação de captcha ultrapassou o prazo total configurado.",
                retryable: true,
                exception,
                attempts: observedAttempts);
        }
        catch (CaptchaException exception) when (action.Optional)
        {
            WriteResult(action, execution, CaptchaSolveResult.Failure(
                KindForAction(action),
                exception.ErrorCode,
                exception.Message,
                exception.Retryable,
                elapsedMs: 0) with
            {
                Attempts = Math.Max(exception.Attempts, observedAttempts)
            });
        }
        catch (PlaywrightException exception) when (action.Optional)
        {
            WriteResult(action, execution, CaptchaSolveResult.Failure(
                KindForAction(action),
                CaptchaErrorCodes.ExtractionFailed,
                exception.Message,
                retryable: true,
                elapsedMs: 0) with
            {
                Attempts = observedAttempts
            });
        }
        catch (Exception exception) when (
            action.Optional &&
            exception is LocatorResolutionException or HttpRequestException or InvalidOperationException)
        {
            var (errorCode, retryable) = exception switch
            {
                HttpRequestException => (CaptchaErrorCodes.UpstreamUnavailable, true),
                LocatorResolutionException => (CaptchaErrorCodes.ExtractionFailed, true),
                _ => (CaptchaErrorCodes.VerificationFailed, false)
            };
            WriteResult(action, execution, CaptchaSolveResult.Failure(
                KindForAction(action),
                errorCode,
                exception.Message,
                retryable,
                elapsedMs: 0) with
            {
                Attempts = observedAttempts
            });
        }
    }

    private static async Task SolveImageCaptchaAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        Action<int> observeAttempts,
        CancellationToken cancellationToken)
    {
        var options = RequireCaptchaOptions(execution);
        var input = await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken);
        var image = await execution.ResolveAsync(
            action.Trigger ?? throw MissingLocator(action, "trigger"),
            LocatorRequiredState.Visible,
            cancellationToken);

        await SolveImageAsync(
            action,
            execution,
            input.Locator.First,
            image.Locator.First,
            challenge: null,
            options,
            observeAttempts,
            cancellationToken);
    }

    private static async Task SolveImageAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        ILocator input,
        ILocator image,
        CaptchaChallenge? challenge,
        CaptchaOptions options,
        Action<int> observeAttempts,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var verification = await CaptureVerificationBaselineAsync(
            action,
            execution,
            cancellationToken);
        var policy = action.Captcha?.SolverPolicy ?? "preferLocal";
        var maximumAttempts = Math.Clamp(action.Captcha?.MaxAttempts ?? 1, 1, 10);
        var attemptsUsed = 0;
        CaptchaSolveResult? inference = null;
        Exception? localFailure = null;
        PixelFrame? pixels = null;
        byte[]? screenshot = null;

        if (!policy.Equals("serviceOnly", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                pixels = await PagePixelsExtractor.TryExtractAsync(
                    image,
                    cancellationToken,
                    options.MaximumImagePixels) ??
                    throw new CaptchaException(
                        CaptchaErrorCodes.ExtractionFailed,
                        "Não foi possível capturar os pixels do captcha de imagem.");
                var modelPath = OcrModelResolver.Resolve(
                    options.OcrModelPath,
                    execution.Context.Options.ConfigurationDirectory);
                using var engine = new ImageOcrEngine(modelPath);
                attemptsUsed++;
                observeAttempts(attemptsUsed);
                var text = engine.Recognize(pixels.Rgba, pixels.Width, pixels.Height).Trim();
                ValidateExpectedAnswer(text, action.Captcha?.ExpectedLength, attemptsUsed);
                inference = new CaptchaSolveResult
                {
                    Status = CaptchaSolveStatus.AnswerProduced,
                    Provider = challenge?.Provider ?? "generic",
                    Kind = CaptchaKind.ImageText,
                    SolverId = "onnx-ctc",
                    ModelVersion = engine.ModelVersion,
                    Answer = text,
                    Actions =
                    [
                        new CaptchaPlannedAction(
                            CaptchaActionKind.TypeText,
                            TargetRole: "response",
                            Text: text)
                    ],
                    Attempts = attemptsUsed,
                    ElapsedMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception exception) when (
                exception is CaptchaException or PlaywrightException)
            {
                localFailure = exception;
            }
        }

        if (inference is null &&
            attemptsUsed < maximumAttempts &&
            !policy.Equals("localOnly", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            screenshot = await image.ScreenshotAsync(
                new LocatorScreenshotOptions { Type = ScreenshotType.Png });
            var snapshotId = Hash(screenshot);
            var challengeId = challenge?.ChallengeId ??
                $"captcha-{snapshotId[..16].ToLowerInvariant()}";
            using var client = new CaptchaServiceClient(options);
            try
            {
                var serviceInference = await client.SolveV2Async(
                    new CaptchaServiceSolveRequest(
                        Guid.NewGuid().ToString("N"),
                        challengeId,
                        snapshotId,
                        "image",
                        new Dictionary<string, object?>
                        {
                            ["imageBase64"] = Convert.ToBase64String(screenshot)
                        },
                        new Dictionary<string, object?>
                        {
                            ["width"] = pixels?.Width,
                            ["height"] = pixels?.Height
                        },
                        Hint: null,
                        DateTimeOffset.UtcNow.AddSeconds(options.DeadlineSeconds),
                        new Dictionary<string, object?>
                        {
                            ["maxAttempts"] = maximumAttempts - attemptsUsed,
                            ["localOnly"] = options.LocalOnly,
                            ["allowVlmFallback"] = options.AllowVlmFallback
                        }),
                    cancellationToken);
                inference = serviceInference with
                {
                    Attempts = ComposeAttempts(attemptsUsed, serviceInference.Attempts)
                };
                observeAttempts(inference.Attempts);
            }
            catch (CaptchaException exception)
            {
                var composed = WithPreviousAttempts(exception, attemptsUsed);
                observeAttempts(composed.Attempts);
                throw composed;
            }
            ValidateExpectedAnswer(
                inference.Answer,
                action.Captcha?.ExpectedLength,
                inference.Attempts);
        }

        if (inference is null)
        {
            var error = localFailure as CaptchaException;
            var result = CaptchaSolveResult.Failure(
                CaptchaKind.ImageText,
                error?.ErrorCode ?? CaptchaErrorCodes.UpstreamUnavailable,
                localFailure?.Message ??
                    "OCR local indisponível e serviço de fallback não configurado.",
                error?.Retryable ?? false,
                stopwatch.ElapsedMilliseconds,
                "onnx-ctc") with
            {
                Attempts = attemptsUsed
            };
            WriteResult(action, execution, result);
            if (!action.Optional)
            {
                throw new CaptchaException(
                    result.ErrorCode!,
                    result.ErrorMessage!,
                    result.Retryable,
                    error,
                    result.Attempts);
            }
            return;
        }

        await EnsureImageSnapshotCurrentAsync(
            image,
            screenshot,
            pixels,
            options.MaximumImagePixels,
            cancellationToken);
        await input.FillAsync(inference.Answer!, new LocatorFillOptions());
        var completed = inference with
        {
            Status = CaptchaSolveStatus.InteractionDone,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            VerificationEvidence = "resposta preenchida; aceite ainda não presumido"
        };
        if (action.Captcha?.VerificationMode?.Equals(
                "solveAndVerify", StringComparison.OrdinalIgnoreCase) == true)
        {
            completed = await VerifyAsync(
                action,
                execution,
                completed,
                verification,
                cancellationToken);
        }
        WriteResult(action, execution, completed);
        if (completed.Status == CaptchaSolveStatus.Failed && !action.Optional)
        {
            throw new CaptchaException(
                completed.ErrorCode!,
                completed.ErrorMessage!,
                completed.Retryable,
                attempts: completed.Attempts);
        }
        Console.WriteLine(
            $"  Captcha de imagem preenchido por {completed.SolverId}; estado {completed.Status}.");
        if (!string.IsNullOrWhiteSpace(action.Output))
        {
            execution.Context.Data.SetRuntimeValue(
                action.Output,
                JsonValue.Create(completed.Answer));
        }
    }

    private static async Task SolveRecaptchaAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        Action<int> observeAttempts,
        CancellationToken cancellationToken)
    {
        var options = RequireCaptchaOptions(execution);
        var stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            var missing = CaptchaSolveResult.Failure(
                CaptchaKind.RecaptchaV2,
                CaptchaErrorCodes.UpstreamUnavailable,
                "solveRecaptchaV2 exige Captcha.ServiceUrl (serviço Python de transcrição).",
                retryable: true,
                stopwatch.ElapsedMilliseconds);
            WriteResult(action, execution, missing);
            if (!action.Optional)
            {
                throw new CaptchaException(
                    missing.ErrorCode!, missing.ErrorMessage!, retryable: true);
            }
            return;
        }

        var outcome = await RecaptchaV2Solver.ExecuteAsync(
            execution.Context.Page,
            options,
            action.Captcha?.MaxAttempts,
            observeAttempts,
            cancellationToken);
        var result = outcome.Solved
            ? new CaptchaSolveResult
            {
                Status = CaptchaSolveStatus.Solved,
                Provider = "google",
                Kind = CaptchaKind.RecaptchaV2,
                SolverId = "recaptcha-v2-audio",
                Attempts = outcome.Attempts,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                VerificationEvidence = "#recaptcha-anchor.recaptcha-checkbox-checked"
            }
            : CaptchaSolveResult.Failure(
                CaptchaKind.RecaptchaV2,
                CaptchaErrorCodes.VerificationFailed,
                "reCAPTCHA v2 não aceito após as tentativas configuradas.",
                retryable: false,
                stopwatch.ElapsedMilliseconds,
                "recaptcha-v2-audio") with
            {
                Attempts = outcome.Attempts
            };
        WriteResult(action, execution, result);
        if (!outcome.Solved && action.Optional == false)
        {
            throw new InvalidOperationException(
                "reCAPTCHA v2 não resolvido após as tentativas configuradas.");
        }

        if (!outcome.Solved)
        {
            Console.WriteLine("  reCAPTCHA v2 não resolvido; ação opcional segue adiante.");
            return;
        }

        Console.WriteLine("  reCAPTCHA v2 resolvido via desafio de áudio.");
    }

    private static async Task SolveHCaptchaAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        Action<int> observeAttempts,
        CancellationToken cancellationToken)
    {
        var options = RequireCaptchaOptions(execution);
        var stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            var missing = CaptchaSolveResult.Failure(
                CaptchaKind.HCaptcha,
                CaptchaErrorCodes.UpstreamUnavailable,
                "solveHCaptcha exige Captcha.ServiceUrl (serviço Python de classificação).",
                retryable: true,
                stopwatch.ElapsedMilliseconds);
            WriteResult(action, execution, missing);
            if (!action.Optional)
            {
                throw new CaptchaException(
                    missing.ErrorCode!, missing.ErrorMessage!, retryable: true);
            }
            return;
        }

        var outcome = await HCaptchaSolver.ExecuteAsync(
            execution.Context.Page,
            options,
            action.Captcha?.MaxAttempts,
            observeAttempts,
            cancellationToken);
        WriteHCaptchaOutcome(action, execution, outcome, stopwatch.ElapsedMilliseconds);
    }

    private static void WriteHCaptchaOutcome(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        HCaptchaSolveOutcome outcome,
        long elapsedMs)
    {
        var result = outcome switch
        {
            { Unsupported: true } => CaptchaSolveResult.Failure(
                CaptchaKind.HCaptcha,
                CaptchaErrorCodes.UnsupportedType,
                "O desafio hCaptcha exibido não é uma grade binária; " +
                "use solveCaptcha para o fallback visual ou a intervenção humana.",
                retryable: false,
                elapsedMs,
                "hcaptcha-resnet-onnx") with
            {
                Attempts = outcome.Attempts
            },
            { Solved: true } => new CaptchaSolveResult
            {
                Status = CaptchaSolveStatus.Solved,
                Provider = "hcaptcha",
                Kind = CaptchaKind.HCaptcha,
                SolverId = "hcaptcha-resnet-onnx",
                Attempts = outcome.Attempts,
                ElapsedMs = elapsedMs,
                VerificationEvidence = "#checkbox[aria-checked='true']"
            },
            _ => CaptchaSolveResult.Failure(
                CaptchaKind.HCaptcha,
                CaptchaErrorCodes.VerificationFailed,
                "hCaptcha não aceito após as tentativas configuradas.",
                retryable: false,
                elapsedMs,
                "hcaptcha-resnet-onnx") with
            {
                Attempts = outcome.Attempts
            }
        };
        WriteResult(action, execution, result);
        if (result.Status == CaptchaSolveStatus.Solved)
        {
            Console.WriteLine("  hCaptcha resolvido via classificação da grade binária.");
            return;
        }
        if (action.Optional)
        {
            Console.WriteLine("  hCaptcha não resolvido; ação opcional segue adiante.");
            return;
        }
        throw new CaptchaException(
            result.ErrorCode!,
            result.ErrorMessage!,
            result.Retryable,
            attempts: result.Attempts);
    }

    private static async Task SolveSliderAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        Action<int> observeAttempts,
        CancellationToken cancellationToken)
    {
        var handle = await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken);
        var background = await execution.ResolveAsync(
            action.Trigger ?? throw MissingLocator(action, "trigger"),
            LocatorRequiredState.Visible,
            cancellationToken);
        var piece = await execution.ResolveAsync(
            action.Options ?? throw MissingLocator(action, "options"),
            LocatorRequiredState.Visible,
            cancellationToken);

        await SolveSliderLocatorsAsync(
            action,
            execution,
            handle.Locator.First,
            background.Locator.First,
            piece.Locator.First,
            challenge: null,
            observeAttempts,
            cancellationToken);
    }

    private static async Task SolveSliderLocatorsAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        ILocator handle,
        ILocator background,
        ILocator piece,
        CaptchaChallenge? challenge,
        Action<int> observeAttempts,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var verification = await CaptureVerificationBaselineAsync(
            action,
            execution,
            cancellationToken);
        var options = execution.Context.Options.Captcha ?? new CaptchaOptions();
        var backgroundPixels = await RequirePixels(
            background, "imagem de fundo", cancellationToken, options.MaximumImagePixels);
        var piecePixels = await RequirePixels(
            piece, "peça do puzzle", cancellationToken, options.MaximumImagePixels);

        var backgroundGray = CaptchaPixels.ToGrayscale(
            backgroundPixels.Rgba,
            backgroundPixels.Width * backgroundPixels.Height);
        var pieceGray = CaptchaPixels.ToGrayscale(
            piecePixels.Rgba,
            piecePixels.Width * piecePixels.Height);
        var pieceMask = CaptchaPixels.ToAlphaMask(
            piecePixels.Rgba,
            piecePixels.Width * piecePixels.Height);

        observeAttempts(1);
        var match = SliderSolver.FindBestMatch(
            backgroundGray,
            backgroundPixels.Width,
            backgroundPixels.Height,
            pieceGray,
            pieceMask,
            piecePixels.Width,
            piecePixels.Height,
            cancellationToken: cancellationToken);
        var threshold = action.Captcha?.MinMatchScore ?? options.SliderMinimumScore;
        if (!double.IsFinite(match.Score) || match.Score < threshold)
        {
            var lowConfidence = CaptchaSolveResult.Failure(
                CaptchaKind.Slider,
                CaptchaErrorCodes.LowConfidence,
                $"Score do slider {match.Score:F3} abaixo do limiar {threshold:F3}.",
                retryable: true,
                stopwatch.ElapsedMilliseconds,
                "normalized-template-matching");
            WriteResult(action, execution, lowConfidence);
            if (!action.Optional)
            {
                throw new CaptchaException(
                    lowConfidence.ErrorCode!,
                    lowConfidence.ErrorMessage!,
                    retryable: true);
            }
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var handleBox = await handle.BoundingBoxAsync() ??
            throw new InvalidOperationException("O puxador do slider não tem bounding box.");
        var backgroundBox = await background.BoundingBoxAsync() ??
            throw new InvalidOperationException("A imagem de fundo do slider não tem bounding box.");
        var pieceBox = await piece.BoundingBoxAsync() ??
            throw new InvalidOperationException("A peça do slider não tem bounding box.");
        if (backgroundBox.Width <= 0 || backgroundBox.Height <= 0)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "A geometria CSS do fundo do slider é inválida.");
        }

        var scaleX = backgroundBox.Width / backgroundPixels.Width;
        var targetOffsetCss = match.BestX * scaleX;
        var pieceOffsetCss = pieceBox.X - backgroundBox.X;
        var deltaX = targetOffsetCss - pieceOffsetCss;
        var actions = new List<CaptchaPlannedAction>();
        if (Math.Abs(deltaX) < 4)
        {
            Console.WriteLine("  Slider observado na posição calculada; aceite ainda não presumido.");
        }
        else
        {
            await EnsurePixelSnapshotCurrentAsync(
                background,
                backgroundPixels,
                options.MaximumImagePixels,
                cancellationToken);
            await EnsurePixelSnapshotCurrentAsync(
                piece,
                piecePixels,
                options.MaximumImagePixels,
                cancellationToken);
            var startX = handleBox.X + handleBox.Width / 2;
            var startY = handleBox.Y + handleBox.Height / 2;
            actions.Add(new CaptchaPlannedAction(
                CaptchaActionKind.Drag,
                TargetRole: "handle",
                X: startX,
                Y: startY,
                ToX: startX + deltaX,
                ToY: startY));
            await HumanizedDrag.ExecuteAsync(
                execution.Context.Page,
                startX,
                startY,
                startX + deltaX,
                cancellationToken);
        }

        var result = new CaptchaSolveResult
        {
            Status = CaptchaSolveStatus.InteractionDone,
            Provider = challenge?.Provider ?? "generic",
            Kind = CaptchaKind.Slider,
            SolverId = "normalized-template-matching",
            Confidence = match.Score,
            Actions = actions,
            Attempts = 1,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            VerificationEvidence = null
        };
        if (action.Captcha?.VerificationMode?.Equals(
                "solveAndVerify", StringComparison.OrdinalIgnoreCase) == true)
        {
            result = await VerifyAsync(
                action,
                execution,
                result,
                verification,
                cancellationToken);
        }
        WriteResult(action, execution, result);
        if (result.Status == CaptchaSolveStatus.Failed && !action.Optional)
        {
            throw new CaptchaException(result.ErrorCode!, result.ErrorMessage!);
        }
        Console.WriteLine(
            $"  Slider interagido {deltaX:N0}px (score {match.Score:F2}, y={match.BestY}); " +
            $"estado {result.Status}.");
    }

    private static async Task SolveAutoAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        Action<int> observeAttempts,
        CancellationToken cancellationToken,
        CancellationToken humanHandoffCancellationToken)
    {
        var detection = await CaptchaDetector.DetectAllAsync(
            execution.Context.Page,
            execution.Context.ExecutionRequest.ExecutionId,
            action.Id,
            cancellationToken);
        if (detection.Status == CaptchaDetector.DetectionStatus.NotPresent)
        {
            WriteResult(action, execution, new CaptchaSolveResult
            {
                Status = CaptchaSolveStatus.NotPresent,
                Kind = CaptchaKind.Unknown,
                Attempts = 0,
                ElapsedMs = 0,
                VerificationEvidence = "nenhum marcador de captcha no escopo observado"
            });
            return;
        }

        var challenges = detection.Challenges;
        if (!string.IsNullOrWhiteSpace(action.Captcha?.Kind))
        {
            if (!TryParseCaptchaKind(action.Captcha.Kind, out var expectedKind))
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.InvalidPayload,
                    $"Modalidade de captcha inválida: '{action.Captcha.Kind}'.");
            }
            var matching = challenges
                .Where(item => item.Challenge.Kind == expectedKind)
                .ToArray();
            if (matching.Length == 0)
            {
                await FinishUncertainAsync(
                    action,
                    execution,
                    $"O desafio detectado não corresponde à modalidade esperada {expectedKind}.");
                return;
            }
            challenges = matching;
        }

        var active = challenges
            .Where(item => item.Challenge.Visible && !item.Challenge.AlreadySolved)
            .ToArray();
        if (active.Length == 0 && challenges.Any(item => item.Challenge.AlreadySolved))
        {
            var solved = challenges.First(item => item.Challenge.AlreadySolved).Challenge;
            WriteResult(action, execution, new CaptchaSolveResult
            {
                Status = CaptchaSolveStatus.Solved,
                Provider = solved.Provider,
                Kind = solved.Kind,
                SolverId = "detector",
                Attempts = 0,
                VerificationEvidence = "widget observado em estado concluído"
            });
            return;
        }
        var candidates = active.Length > 0
            ? active
            : challenges
                .Where(item => !item.Challenge.AlreadySolved)
                .ToArray();
        if (candidates.Length != 1)
        {
            await FinishUncertainAsync(
                action,
                execution,
                "A detecção encontrou zero ou múltiplos desafios ativos; informe escopo/receita.");
            return;
        }

        var detected = candidates[0];
        switch (detected.Challenge.Kind)
        {
            case CaptchaKind.RecaptchaV2 when detected.Challenge.Variant == "v2-invisible":
                await WaitHumanInputAsync(
                    action,
                    execution,
                    humanHandoffCancellationToken,
                    detected.Challenge,
                    "reCAPTCHA v2 invisível detectado sem adapter automático seguro.");
                return;
            case CaptchaKind.RecaptchaV2:
                await SolveRecaptchaAsync(
                    action,
                    execution,
                    observeAttempts,
                    cancellationToken);
                return;
            case CaptchaKind.ImageText when
                detected.Primary is not null && detected.Secondary is not null:
                await SolveImageAsync(
                    action,
                    execution,
                    detected.Primary,
                    detected.Secondary,
                    detected.Challenge,
                    RequireCaptchaOptions(execution),
                    observeAttempts,
                    cancellationToken);
                return;
            case CaptchaKind.Slider when
                detected.Primary is not null &&
                detected.Secondary is not null &&
                detected.Tertiary is not null:
                await SolveSliderLocatorsAsync(
                    action,
                    execution,
                    detected.Primary,
                    detected.Secondary,
                    detected.Tertiary,
                    detected.Challenge,
                    observeAttempts,
                    cancellationToken);
                return;
            case CaptchaKind.CloudflareTurnstile:
            case CaptchaKind.FriendlyCaptcha:
            case CaptchaKind.CloudflareChallenge:
                await SolveSamePageAsync(
                    action,
                    execution,
                    detected,
                    observeAttempts,
                    cancellationToken,
                    humanHandoffCancellationToken);
                return;
            case CaptchaKind.HCaptcha:
                await SolveHCaptchaAutoAsync(
                    action,
                    execution,
                    detected,
                    observeAttempts,
                    cancellationToken,
                    humanHandoffCancellationToken);
                return;
            case CaptchaKind.RecaptchaEnterprise:
            case CaptchaKind.ArkoseFunCaptcha:
            case CaptchaKind.GeeTest:
            case CaptchaKind.AwsWaf:
                await SolveVisualOrHumanAsync(
                    action,
                    execution,
                    detected,
                    observeAttempts,
                    cancellationToken,
                    humanHandoffCancellationToken,
                    $"{detected.Challenge.Provider}/{detected.Challenge.Kind} exige intervenção humana.");
                return;
            case CaptchaKind.RecaptchaV3:
                var unsupported = new CaptchaSolveResult
                {
                    Status = CaptchaSolveStatus.Unsupported,
                    Provider = detected.Challenge.Provider,
                    Kind = detected.Challenge.Kind,
                    SolverId = "router",
                    ErrorCode = CaptchaErrorCodes.UnsupportedType,
                    ErrorMessage = "Modalidade conhecida sem adapter habilitado.",
                    Retryable = false,
                    Attempts = 0
                };
                WriteResult(action, execution, unsupported);
                if (!action.Optional)
                {
                    throw new CaptchaException(
                        unsupported.ErrorCode!, unsupported.ErrorMessage!);
                }
                return;
            case CaptchaKind.ImageText:
            case CaptchaKind.Slider:
            case CaptchaKind.Unknown:
            default:
                await FinishUncertainAsync(
                    action,
                    execution,
                    "O desafio foi identificado, mas a receita não fornece locators únicos suficientes.");
                return;
        }
    }

    private static async Task SolveHCaptchaAutoAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        DetectedCaptcha detected,
        Action<int> observeAttempts,
        CancellationToken cancellationToken,
        CancellationToken humanHandoffCancellationToken)
    {
        var options = RequireCaptchaOptions(execution);
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            await FallbackHCaptchaVisualOrHumanAsync(
                action,
                execution,
                detected,
                observeAttempts,
                cancellationToken,
                humanHandoffCancellationToken);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        HCaptchaSolveOutcome outcome;
        try
        {
            outcome = await HCaptchaSolver.ExecuteAsync(
                execution.Context.Page,
                options,
                action.Captcha?.MaxAttempts,
                observeAttempts,
                cancellationToken);
        }
        catch (CaptchaException exception) when (
            exception.ErrorCode is CaptchaErrorCodes.ModelMissing or
                CaptchaErrorCodes.UnsupportedType)
        {
            observeAttempts(exception.Attempts);
            // Capacidade ou rótulo indisponível: fallback VLM ou intervenção humana.
            await FallbackHCaptchaVisualOrHumanAsync(
                action,
                execution,
                detected,
                observeAttempts,
                cancellationToken,
                humanHandoffCancellationToken);
            return;
        }

        if (outcome.Unsupported)
        {
            await FallbackHCaptchaVisualOrHumanAsync(
                action,
                execution,
                detected,
                observeAttempts,
                cancellationToken,
                humanHandoffCancellationToken);
            return;
        }
        WriteHCaptchaOutcome(action, execution, outcome, stopwatch.ElapsedMilliseconds);
    }

    private static Task FallbackHCaptchaVisualOrHumanAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        DetectedCaptcha detected,
        Action<int> observeAttempts,
        CancellationToken cancellationToken,
        CancellationToken humanHandoffCancellationToken) =>
        detected.Challenge.Variant != "challenge"
            ? WaitHumanInputAsync(
                action,
                execution,
                humanHandoffCancellationToken,
                detected.Challenge,
                "hCaptcha detectado antes da superfície visual do desafio; " +
                "intervenção humana necessária.")
            : SolveVisualOrHumanAsync(
                action,
                execution,
                detected,
                observeAttempts,
                cancellationToken,
                humanHandoffCancellationToken,
                $"{detected.Challenge.Provider}/{detected.Challenge.Kind} exige intervenção humana.");

    private static async Task SolveVisualOrHumanAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        DetectedCaptcha detected,
        Action<int> observeAttempts,
        CancellationToken cancellationToken,
        CancellationToken humanHandoffCancellationToken,
        string handoffMessage)
    {
        var options = RequireCaptchaOptions(execution);
        if (!options.AllowVlmFallback || string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            await WaitHumanInputAsync(
                action,
                execution,
                humanHandoffCancellationToken,
                detected.Challenge,
                handoffMessage);
            return;
        }

        var visualAttempts = 0;
        void ObserveVisualAttempts(int value)
        {
            visualAttempts = Math.Max(visualAttempts, Math.Clamp(value, 0, 10));
            observeAttempts(value);
        }

        try
        {
            var verification = await CaptureVerificationBaselineAsync(
                action,
                execution,
                cancellationToken);
            var policy = action.Captcha?.SolverPolicy ?? "preferLocal";
            var result = await VisualCaptchaAdapter.ExecuteAsync(
                execution.Context.Page,
                detected,
                options,
                options.LocalOnly || policy.Equals(
                    "localOnly", StringComparison.OrdinalIgnoreCase),
                action.Captcha?.MaxAttempts ?? 1,
                hint: null,
                ObserveVisualAttempts,
                cancellationToken);
            if (action.Captcha?.VerificationMode?.Equals(
                    "solveAndVerify", StringComparison.OrdinalIgnoreCase) == true)
            {
                result = await VerifyAsync(
                    action,
                    execution,
                    result,
                    verification,
                    cancellationToken);
            }
            WriteResult(action, execution, result);
            if (result.Status == CaptchaSolveStatus.Failed && !action.Optional)
            {
                throw new CaptchaException(
                    result.ErrorCode!,
                    result.ErrorMessage!,
                    result.Retryable,
                    attempts: result.Attempts);
            }
        }
        catch (Exception exception) when (
            exception is CaptchaException or PlaywrightException)
        {
            var error = exception as CaptchaException;
            var attempts = Math.Max(error?.Attempts ?? 0, visualAttempts);
            WriteResult(action, execution, CaptchaSolveResult.Failure(
                detected.Challenge.Kind,
                error?.ErrorCode ?? CaptchaErrorCodes.ExtractionFailed,
                exception.Message,
                error?.Retryable ?? true,
                elapsedMs: 0,
                "litellm-vlm") with
            {
                Attempts = attempts
            });
            await WaitHumanInputAsync(
                action,
                execution,
                humanHandoffCancellationToken,
                detected.Challenge,
                handoffMessage + $" Fallback visual indisponível: {exception.Message}",
                attempts);
        }
    }

    private static async Task SolveSamePageAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        DetectedCaptcha detected,
        Action<int> observeAttempts,
        CancellationToken cancellationToken,
        CancellationToken humanHandoffCancellationToken)
    {
        var options = RequireCaptchaOptions(execution);
        var verification = await CaptureVerificationBaselineAsync(
            action,
            execution,
            cancellationToken);
        var configuredSeconds = Math.Min(
            options.DeadlineSeconds,
            options.SamePageWaitSeconds);
        if (action.TimeoutMs is { } timeoutMs && timeoutMs > 0)
        {
            configuredSeconds = Math.Min(
                configuredSeconds,
                Math.Max(1, timeoutMs / 1_000));
        }
        if (configuredSeconds <= 0)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "Captcha.SamePageWaitSeconds e DeadlineSeconds devem ser positivos.");
        }

        observeAttempts(1);
        var result = await SamePageCaptchaAdapter.ExecuteAsync(
            execution.Context.Page,
            detected,
            action.Captcha?.AllowInteractiveClick == true,
            TimeSpan.FromSeconds(configuredSeconds),
            cancellationToken);
        if (result.Status == CaptchaSolveStatus.NeedsHuman)
        {
            if (detected.Challenge.Kind == CaptchaKind.CloudflareChallenge &&
                action.Captcha?.AllowCloudflareSidecar == true &&
                options.CloudflareSidecarEnabled)
            {
                try
                {
                    var sidecarResult = await CloudflareSidecarAdapter.ExecuteAsync(
                        execution.Context.Page,
                        detected,
                        options,
                        cancellationToken);
                    result = sidecarResult with
                    {
                        Attempts = ComposeAttempts(result.Attempts, sidecarResult.Attempts)
                    };
                    observeAttempts(result.Attempts);
                    if (result.Status == CaptchaSolveStatus.InteractionDone &&
                        action.Captcha.VerificationMode?.Equals(
                            "solveAndVerify", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        result = await VerifyAsync(
                            action,
                            execution,
                            result,
                            verification,
                            cancellationToken);
                    }
                    WriteResult(action, execution, result);
                    if (result.Status is CaptchaSolveStatus.InteractionDone or
                        CaptchaSolveStatus.Solved)
                    {
                        return;
                    }
                }
                catch (Exception exception) when (
                    exception is CaptchaException or PlaywrightException)
                {
                    var error = exception as CaptchaException;
                    result = CaptchaSolveResult.Failure(
                        CaptchaKind.CloudflareChallenge,
                        error?.ErrorCode ?? CaptchaErrorCodes.ExtractionFailed,
                        exception.Message,
                        error?.Retryable ?? true,
                        elapsedMs: 0,
                        options.CloudflareSidecarProvider + "-artifact-transfer") with
                    {
                        Attempts = ComposeAttempts(result.Attempts, error?.Attempts ?? 1)
                    };
                    observeAttempts(result.Attempts);
                }
            }
            WriteResult(action, execution, result);
            await WaitHumanInputAsync(
                action,
                execution,
                humanHandoffCancellationToken,
                detected.Challenge,
                $"{detected.Challenge.Provider}/{detected.Challenge.Kind} " +
                "não concluiu na sessão; intervenção humana necessária." +
                (string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? string.Empty
                    : $" Motivo: {result.ErrorMessage}"),
                result.Attempts);
            return;
        }

        if (action.Captcha?.VerificationMode?.Equals(
                "solveAndVerify", StringComparison.OrdinalIgnoreCase) == true)
        {
            result = await VerifyAsync(
                action,
                execution,
                result,
                verification,
                cancellationToken);
        }
        WriteResult(action, execution, result);
        if (result.Status == CaptchaSolveStatus.Failed && !action.Optional)
        {
            throw new CaptchaException(
                result.ErrorCode!,
                result.ErrorMessage!,
                result.Retryable,
                attempts: result.Attempts);
        }
    }

    private static async Task WaitHumanInputAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken,
        CaptchaChallenge? challenge = null,
        string? overrideMessage = null,
        int previousAttempts = 0)
    {
        var options = execution.Context.Options.Captcha ?? new CaptchaOptions();
        var verification = await CaptureVerificationBaselineAsync(
            action,
            execution,
            cancellationToken);
        var message = overrideMessage ??
            FlowValueResolver.ResolveOptional(action, execution.Context.Data) ??
            action.Name;

        var timeout = TimeSpan.FromSeconds(
            action.TimeoutMs is { } ms && ms > 0
                ? ms / 1_000.0
                : options.HumanHandoffTimeoutSeconds);
        var poll = TimeSpan.FromSeconds(options.HumanHandoffPollSeconds);

        string? screenshotPath = null;
        try
        {
            screenshotPath = await execution.Context.Artifacts.CaptureSanitizedScreenshotAsync(
                "intervencao-humana");
        }
        catch (PlaywrightException)
        {
            // a evidência é auxiliar
        }

        var pending = new CaptchaSolveResult
        {
            Status = CaptchaSolveStatus.NeedsHuman,
            Provider = challenge?.Provider,
            Kind = challenge?.Kind ?? CaptchaKind.Unknown,
            SolverId = "human-handoff",
            Actions =
            [
                new CaptchaPlannedAction(
                    CaptchaActionKind.HumanHandoff,
                    TargetRole: "challenge")
            ],
            Attempts = Math.Clamp(previousAttempts, 0, 10),
            VerificationEvidence = "solicitação de intervenção humana criada"
        };

        string? requestId = null;
        try
        {
            await HumanHandoff.ExecuteAsync(
                new HumanHandoffContext(
                    execution.Context.ExecutionRequest.ExecutionId,
                    action.Id,
                    challenge?.ChallengeId ?? $"manual-{action.Id}",
                    challenge?.Provider,
                    challenge?.Kind ?? CaptchaKind.Unknown),
                message,
                ResolveOutputDirectory(execution.Context.Options),
                timeout,
                poll,
                screenshotPath is null ? [] : [screenshotPath],
                cancellationToken,
                async (request, token) =>
                {
                    requestId = request.RequestId;
                    WriteResult(action, execution, pending);
                    await ObserveHumanHandoffAsync(
                        execution,
                        action,
                        challenge,
                        "captchaHumanHandoffRequested",
                        requestId,
                        token);
                });
            await ObserveHumanHandoffAsync(
                execution,
                action,
                challenge,
                "captchaHumanHandoffCompleted",
                requestId,
                cancellationToken);
        }
        catch (TimeoutException exception)
        {
            var expired = pending with
            {
                Status = CaptchaSolveStatus.Expired,
                ErrorCode = CaptchaErrorCodes.DeadlineExceeded,
                ErrorMessage = exception.Message,
                Retryable = true
            };
            WriteResult(action, execution, expired);
            if (action.Optional)
            {
                await ObserveHumanHandoffAsync(
                    execution,
                    action,
                    challenge,
                    "captchaHumanHandoffCompleted",
                    requestId,
                    CancellationToken.None);
            }
            if (!action.Optional)
            {
                throw;
            }
            return;
        }
        catch (HumanHandoffRejectedException exception)
        {
            await ObserveHumanHandoffAsync(
                execution,
                action,
                challenge,
                "captchaHumanHandoffCompleted",
                requestId,
                CancellationToken.None);
            var rejected = pending with
            {
                Status = CaptchaSolveStatus.Failed,
                ErrorCode = CaptchaErrorCodes.VerificationFailed,
                ErrorMessage = exception.Message,
                Retryable = false
            };
            WriteResult(action, execution, rejected);
            if (!action.Optional)
            {
                throw;
            }
            return;
        }
        catch (OperationCanceledException)
        {
            WriteResult(action, execution, pending with
            {
                Status = CaptchaSolveStatus.Cancelled,
                ErrorCode = CaptchaErrorCodes.Cancelled,
                ErrorMessage = "A intervenção humana foi cancelada.",
                Retryable = true
            });
            throw;
        }

        CaptchaSolveResult completed;
        if (challenge is null)
        {
            completed = pending with
            {
                Status = CaptchaSolveStatus.InteractionDone,
                Attempts = Math.Max(1, pending.Attempts),
                VerificationEvidence =
                    "operador confirmou a intervenção genérica; pós-condição não presumida"
            };
        }
        else
        {
            completed = pending with
            {
                Status = CaptchaSolveStatus.InteractionDone,
                Attempts = Math.Max(1, pending.Attempts),
                VerificationEvidence = "operador confirmou a interação; aceite do desafio não presumido"
            };
            if (action.Captcha?.VerificationMode?.Equals(
                    "solveAndVerify", StringComparison.OrdinalIgnoreCase) == true)
            {
                completed = await VerifyAsync(
                    action,
                    execution,
                    completed,
                    verification,
                    cancellationToken);
            }
        }

        WriteResult(action, execution, completed);
        if (completed.Status == CaptchaSolveStatus.Failed && !action.Optional)
        {
            throw new CaptchaException(
                completed.ErrorCode!,
                completed.ErrorMessage!,
                completed.Retryable,
                attempts: completed.Attempts);
        }
    }

    private static ValueTask ObserveHumanHandoffAsync(
        V2FlowActionExecutionScope execution,
        FlowActionDefinition action,
        CaptchaChallenge? challenge,
        string kind,
        string? requestId,
        CancellationToken cancellationToken)
    {
        var request = execution.Context.ExecutionRequest;
        return execution.Context.ObserveAsync(
            new FlowExecutionEvent(
                kind,
                request.ExecutionId,
                request.WorkItemId,
                request.BatchId,
                DateTimeOffset.UtcNow,
                action.Id,
                action.Name,
                action.Type,
                execution.Context.ExecutionBudget.ExecutedActions,
                Detail: requestId ?? challenge?.ChallengeId ?? $"manual-{action.Id}"),
            cancellationToken);
    }

    private static Task FinishUncertainAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        string message)
    {
        var result = new CaptchaSolveResult
        {
            Status = CaptchaSolveStatus.Uncertain,
            Kind = CaptchaKind.Unknown,
            SolverId = "detector",
            ErrorCode = CaptchaErrorCodes.NeedsConfiguration,
            ErrorMessage = message,
            Retryable = false,
            Attempts = 0
        };
        WriteResult(action, execution, result);
        if (!action.Optional)
        {
            throw new CaptchaException(result.ErrorCode!, result.ErrorMessage!);
        }
        return Task.CompletedTask;
    }

    private static async Task<CaptchaSolveResult> VerifyAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CaptchaSolveResult result,
        VerificationBaseline? baseline,
        CancellationToken cancellationToken)
    {
        var locator = action.Success ?? action.Ready;
        var role = action.Success is not null ? "success" : "ready";
        if (locator is null)
        {
            return result with
            {
                Status = CaptchaSolveStatus.Failed,
                ErrorCode = CaptchaErrorCodes.NeedsConfiguration,
                ErrorMessage =
                    "verificationMode=solveAndVerify exige locator success ou ready.",
                Retryable = false,
                VerificationEvidence = null
            };
        }
        if (baseline is null || baseline.WasVisible)
        {
            return result with
            {
                Status = CaptchaSolveStatus.Failed,
                ErrorCode = CaptchaErrorCodes.VerificationFailed,
                ErrorMessage = baseline is null
                    ? "A pós-condição do captcha não pôde ser observada antes da interação."
                    : $"O locator {baseline.Role} já estava visível antes da interação; " +
                      "não há transição que comprove o aceite.",
                Retryable = false,
                VerificationEvidence = null
            };
        }

        try
        {
            _ = await execution.ResolveAsync(
                locator,
                LocatorRequiredState.Visible,
                cancellationToken,
                action.TimeoutMs);
            return result with
            {
                Status = CaptchaSolveStatus.Solved,
                ErrorCode = null,
                ErrorMessage = null,
                Retryable = false,
                VerificationEvidence =
                    $"locator {role} transitou de ausente/oculto para visível"
            };
        }
        catch (Exception exception) when (
            exception is PlaywrightException or TimeoutException or InvalidOperationException)
        {
            return result with
            {
                Status = CaptchaSolveStatus.Failed,
                ErrorCode = CaptchaErrorCodes.VerificationFailed,
                ErrorMessage = $"A pós-condição do captcha não foi observada: {exception.Message}",
                Retryable = true,
                VerificationEvidence = null
            };
        }
    }

    private static async Task<VerificationBaseline?> CaptureVerificationBaselineAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        if (action.Captcha?.VerificationMode?.Equals(
                "solveAndVerify", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        var locator = action.Success ?? action.Ready;
        if (locator is null)
        {
            return null;
        }

        var role = action.Success is not null ? "success" : "ready";
        var resolved = await execution.ResolveAsync(
            locator,
            LocatorRequiredState.Any,
            cancellationToken,
            timeoutMs: 100,
            allowEmpty: true);
        var wasVisible = await resolved.Locator.First.IsVisibleAsync();
        return new VerificationBaseline(role, wasVisible);
    }

    private static async Task EnsureImageSnapshotCurrentAsync(
        ILocator image,
        byte[]? screenshot,
        PixelFrame? pixels,
        int maximumPixels,
        CancellationToken cancellationToken)
    {
        if (screenshot is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await image.ScreenshotAsync(
                new LocatorScreenshotOptions { Type = ScreenshotType.Png });
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(screenshot),
                    SHA256.HashData(current)))
            {
                throw StaleImageSnapshot();
            }
            return;
        }

        if (pixels is not null)
        {
            await EnsurePixelSnapshotCurrentAsync(
                image,
                pixels,
                maximumPixels,
                cancellationToken);
        }
    }

    private static async Task EnsurePixelSnapshotCurrentAsync(
        ILocator locator,
        PixelFrame expected,
        int maximumPixels,
        CancellationToken cancellationToken)
    {
        var current = await PagePixelsExtractor.TryExtractAsync(
            locator,
            cancellationToken,
            maximumPixels) ?? throw StaleImageSnapshot();
        if (current.Width != expected.Width ||
            current.Height != expected.Height ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(expected.Rgba),
                SHA256.HashData(current.Rgba)))
        {
            throw StaleImageSnapshot();
        }
    }

    private static CaptchaException StaleImageSnapshot() =>
        new(
            CaptchaErrorCodes.StaleSnapshot,
            "A imagem do captcha mudou durante a inferência; nenhuma interação foi executada.",
            retryable: true);

    internal static void ValidateExpectedAnswer(
        string? answer,
        int? expectedLength,
        int attempts = 1)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.DecodeFailed,
                "O OCR não produziu uma resposta textual válida.",
                retryable: true,
                attempts: attempts);
        }
        if (expectedLength is not null && answer.Length != expectedLength)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.LowConfidence,
                $"O OCR produziu {answer.Length} caracteres; eram esperados {expectedLength}.",
                retryable: true,
                attempts: attempts);
        }
    }

    internal static int ComposeAttempts(int previousAttempts, int currentAttempts) =>
        Math.Clamp(previousAttempts + currentAttempts, 0, 10);

    private static CaptchaException WithPreviousAttempts(
        CaptchaException exception,
        int previousAttempts) =>
        new(
            exception.ErrorCode,
            exception.Message,
            exception.Retryable,
            exception,
            ComposeAttempts(previousAttempts, exception.Attempts));

    private static bool TryParseCaptchaKind(string value, out CaptchaKind kind)
    {
        kind = value.ToLowerInvariant() switch
        {
            "imagetext" => CaptchaKind.ImageText,
            "slider" => CaptchaKind.Slider,
            "recaptchav2" => CaptchaKind.RecaptchaV2,
            "recaptchav3" => CaptchaKind.RecaptchaV3,
            "recaptchaenterprise" => CaptchaKind.RecaptchaEnterprise,
            "hcaptcha" => CaptchaKind.HCaptcha,
            "turnstile" => CaptchaKind.CloudflareTurnstile,
            "cloudflarechallenge" => CaptchaKind.CloudflareChallenge,
            "arkosefuncaptcha" => CaptchaKind.ArkoseFunCaptcha,
            "geetest" => CaptchaKind.GeeTest,
            "awswaf" => CaptchaKind.AwsWaf,
            "friendlycaptcha" => CaptchaKind.FriendlyCaptcha,
            _ => CaptchaKind.Unknown
        };
        return kind != CaptchaKind.Unknown;
    }

    private static CaptchaKind KindForAction(FlowActionDefinition action)
    {
        if (!string.IsNullOrWhiteSpace(action.Captcha?.Kind) &&
            TryParseCaptchaKind(action.Captcha.Kind, out var configured))
        {
            return configured;
        }
        return action.Type.ToLowerInvariant() switch
        {
            "solveimagecaptcha" => CaptchaKind.ImageText,
            "solveslidercaptcha" => CaptchaKind.Slider,
            "solverecaptchav2" => CaptchaKind.RecaptchaV2,
            "solvehcaptcha" => CaptchaKind.HCaptcha,
            _ => CaptchaKind.Unknown
        };
    }

    private static void WriteResult(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CaptchaSolveResult result)
    {
        if (string.IsNullOrWhiteSpace(action.Captcha?.ResultOutput))
        {
            return;
        }

        execution.Context.Data.SetRuntimeValue(
            action.Captcha.ResultOutput,
            JsonSerializer.SerializeToNode(result, ResultJsonOptions));
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static string ResolveOutputDirectory(PlaywrightRuntimeOptions options)
    {
        var configuredPath = options.OutputDirectory;
        if (Path.IsPathFullyQualified(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        if (Path.IsPathRooted(configuredPath))
        {
            throw new InvalidOperationException(
                "OutputDirectory deve ser relativo à configuração ou absoluto.");
        }

        return Path.GetFullPath(
            Path.Combine(options.ConfigurationDirectory, configuredPath));
    }

    private static CaptchaOptions RequireCaptchaOptions(
        V2FlowActionExecutionScope execution) =>
        execution.Context.Options.Captcha ??
        throw new InvalidOperationException(
            "A execução não tem seção Captcha configurada em Runtime.");

    private static async Task<PixelFrame> RequirePixels(
        ILocator locator,
        string role,
        CancellationToken cancellationToken,
        int maximumPixels = CaptchaPixels.MaximumPixels) =>
        await PagePixelsExtractor.TryExtractAsync(
            locator,
            cancellationToken,
            maximumPixels) ??
        throw new CaptchaException(
            CaptchaErrorCodes.ExtractionFailed,
            $"Não consegui extrair pixels da {role}.");

    private static InvalidOperationException MissingLocator(
        FlowActionDefinition action,
        string role) =>
        new($"A ação '{action.Name}' não informou o locator de papel '{role}'.");

    private sealed record VerificationBaseline(string Role, bool WasVisible);
}
