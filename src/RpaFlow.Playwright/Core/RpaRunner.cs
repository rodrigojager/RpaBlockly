using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public sealed class RpaRunner(
    IReadOnlyList<IRpaStep> steps,
    PlaywrightRuntimeOptions options,
    IPagePolicyFactory? pagePolicyFactory = null,
    IFlowExecutionObserver? observer = null,
    IFlowActionExecutionGuard? executionGuard = null,
    IOneTimeCodeProvider? oneTimeCodeProvider = null,
    TimeProvider? timeProvider = null)
{
    private readonly IPagePolicyFactory _pagePolicyFactory =
        pagePolicyFactory ?? DefaultPagePolicyFactory.Instance;
    private readonly IFlowExecutionObserver _observer =
        observer ?? NullFlowExecutionObserver.Instance;
    private readonly IFlowActionExecutionGuard _executionGuard =
        executionGuard ?? NullFlowActionExecutionGuard.Instance;
    private readonly IOneTimeCodeProvider? _oneTimeCodeProvider = oneTimeCodeProvider;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<FlowExecutionResult> RunAsync(
        FlowExecutionRequest executionRequest,
        IReadOnlyList<FlowInputRequirementDefinition> inputRequirements,
        CancellationToken cancellationToken)
    {
        FlowExecutionRequestValidator.Validate(executionRequest);
        var startedAt = DateTimeOffset.UtcNow;
        await ObserveSafelyAsync(
            new FlowExecutionEvent(
                "executionStarted",
                executionRequest.ExecutionId,
                executionRequest.WorkItemId,
                executionRequest.BatchId,
                startedAt),
            cancellationToken);

        try
        {
            try
            {
                PlaywrightRuntimeOptionsValidator.Validate(options);
                FlowInputValidator.Validate(
                    inputRequirements,
                    new FlowDataContext(executionRequest));
                _ = ResolveOutputDirectory(options);
                _ = ResolveStorageStatePath(options);
            }
            catch (Exception exception)
            {
                throw FlowFailureClassifier.ForExecution(
                    executionRequest,
                    exception,
                    preflight: true);
            }

            var result = await RunCoreAsync(
                executionRequest,
                startedAt,
                cancellationToken);
            var observationToken = options.HoldBrowserOpenForInspection
                ? CancellationToken.None
                : cancellationToken;
            await ObserveSafelyAsync(
                new FlowExecutionEvent(
                    "executionCompleted",
                    executionRequest.ExecutionId,
                    executionRequest.WorkItemId,
                    executionRequest.BatchId,
                    result.CompletedAtUtc ?? DateTimeOffset.UtcNow,
                    ExecutedActions: result.ExecutedActions,
                    ElapsedMilliseconds: result.CompletedAtUtc.HasValue &&
                        result.StartedAtUtc.HasValue
                            ? (long)(result.CompletedAtUtc.Value -
                                result.StartedAtUtc.Value).TotalMilliseconds
                            : null),
                observationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            await ObserveSafelyAsync(
                new FlowExecutionEvent(
                    "executionCancelled",
                    executionRequest.ExecutionId,
                    executionRequest.WorkItemId,
                    executionRequest.BatchId,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
            throw;
        }
        catch (FlowExecutionException exception)
        {
            var observationToken = options.HoldBrowserOpenForInspection
                ? CancellationToken.None
                : cancellationToken;
            await ObserveFailureAsync(exception.Failure, observationToken);
            throw;
        }
        catch (Exception exception)
        {
            var classified = FlowFailureClassifier.ForExecution(
                executionRequest,
                exception);
            var observationToken = options.HoldBrowserOpenForInspection
                ? CancellationToken.None
                : cancellationToken;
            await ObserveFailureAsync(classified.Failure, observationToken);
            throw classified;
        }
    }

    private async Task<FlowExecutionResult> RunCoreAsync(
        FlowExecutionRequest executionRequest,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using var session = await BrowserLauncher.LaunchAsync(options);
        var storageStatePath = ResolveStorageStatePath(options);

        var browserContext = await session.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                AcceptDownloads = true,
                Locale = options.Locale,
                StorageStatePath = storageStatePath is not null && File.Exists(storageStatePath)
                    ? storageStatePath
                    : null,
                ViewportSize = new ViewportSize
                {
                    Width = options.ViewportWidth,
                    Height = options.ViewportHeight
                }
            });

        try
        {
            var page = await browserContext.NewPageAsync();
            var outputDirectory = ResolveOutputDirectory(options);
            using var context = new RpaContext(
                page,
                options,
                executionRequest,
                outputDirectory,
                _pagePolicyFactory,
                _observer,
                _executionGuard,
                _oneTimeCodeProvider,
                _timeProvider);

            try
            {
                for (var index = 0; index < steps.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Console.WriteLine($"[{index + 1}/{steps.Count}] {steps[index].Name}");
                    await steps[index].ExecuteAsync(context, cancellationToken);
                }
            }
            catch (FlowExecutionCompletedSignalException signal)
            {
                Console.WriteLine(
                    $"Execução concluída pelo guard após a ação '{signal.ActionId}'.");
            }
            catch
            {
                try
                {
                    var failureScreenshot =
                        await context.Artifacts.CaptureScreenshotAsync("falha");
                    Console.Error.WriteLine($"Evidência da falha: {failureScreenshot}");
                }
                catch
                {
                    // A captura é auxiliar e não deve ocultar a falha original.
                }

                if (options.HoldBrowserOpenForInspection)
                {
                    await HoldBrowserOpenForInspectionAsync(cancellationToken);
                }

                throw;
            }

            if (options.SaveStorageState && storageStatePath is not null)
            {
                await SaveStorageStateAsync(
                    browserContext,
                    storageStatePath,
                    cancellationToken);
            }

            var completedAt = DateTimeOffset.UtcNow;
            var result = new FlowExecutionResult(
                executionRequest.ExecutionId,
                executionRequest.WorkItemId,
                executionRequest.BatchId,
                context.Data.ExportRuntime(),
                startedAt,
                completedAt,
                context.ExecutionBudget.ExecutedActions);

            if (options.HoldBrowserOpenForInspection)
            {
                await HoldBrowserOpenForInspectionAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            await browserContext.CloseAsync();
        }
    }

    private Task ObserveFailureAsync(
        FlowExecutionFailure failure,
        CancellationToken cancellationToken) =>
        ObserveSafelyAsync(
            new FlowExecutionEvent(
                "executionFailed",
                failure.ExecutionId,
                failure.WorkItemId,
                failure.BatchId,
                failure.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                failure.ActionId,
                failure.ActionName,
                failure.ActionType,
                FailureCategory: failure.Category,
                Retryable: failure.Retryable),
            cancellationToken);

    private async Task ObserveSafelyAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await _observer.ObserveAsync(executionEvent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Observador de execução falhou sem interromper o RPA: {exception.Message}");
        }
    }

    private static string ResolveOutputDirectory(PlaywrightRuntimeOptions runtimeOptions)
    {
        var configuredPath = runtimeOptions.OutputDirectory;
        if (Path.IsPathFullyQualified(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        if (Path.IsPathRooted(configuredPath))
        {
            throw new InvalidOperationException(
                "OutputDirectory deve ser relativo à configuração ou " +
                "um caminho absoluto totalmente qualificado, inclusive UNC.");
        }

        return Path.GetFullPath(
            Path.Combine(runtimeOptions.ConfigurationDirectory, configuredPath));
    }

    private static string? ResolveStorageStatePath(
        PlaywrightRuntimeOptions runtimeOptions)
    {
        var configuredPath = runtimeOptions.StorageStatePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            if (runtimeOptions.SaveStorageState)
            {
                throw new InvalidOperationException(
                    "SaveStorageState exige StorageStatePath.");
            }

            return null;
        }

        if (Path.IsPathFullyQualified(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        if (Path.IsPathRooted(configuredPath))
        {
            throw new InvalidOperationException(
                "StorageStatePath deve ser relativo à configuração ou " +
                "um caminho absoluto totalmente qualificado.");
        }

        var configurationRoot = Path.GetFullPath(runtimeOptions.ConfigurationDirectory);
        var resolvedPath = Path.GetFullPath(
            Path.Combine(configurationRoot, configuredPath));
        var confinedRoot = configurationRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolvedPath.StartsWith(confinedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "StorageStatePath relativo não pode escapar da pasta de configuração.");
        }

        return resolvedPath;
    }

    private static async Task SaveStorageStateAsync(
        IBrowserContext browserContext,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException(
                "StorageStatePath não possui uma pasta válida.");
        Directory.CreateDirectory(directory);
        var temporaryPath = destinationPath + $".tmp-{Guid.NewGuid():N}";

        try
        {
            await browserContext.StorageStateAsync(
                new BrowserContextStorageStateOptions { Path = temporaryPath });
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task HoldBrowserOpenForInspectionAsync(
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            throw new InvalidOperationException(
                "O modo de inspeção exige um token cancelável para liberar o navegador.");
        }

        Console.WriteLine(
            "Modo de inspeção ativo: o navegador permanecerá aberto. " +
            "Pressione Ctrl+C quando terminar de inspecionar.");

        var released = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            released);
        await released.Task.ConfigureAwait(false);
        Console.WriteLine("Inspeção encerrada; fechando o navegador.");
    }
}
