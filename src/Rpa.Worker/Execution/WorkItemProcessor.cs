using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Rpa.Worker.Configuration;
using Rpa.Worker.Data;
using Rpa.Worker.Domain;
using Rpa.Worker.Storage;
using RpaFlow.Packages;
using RpaFlow.Playwright;
using RpaFlow.Playwright.V2;
using RpaFlow.Playwright.V2.Adaptive;
using RpaFlow.Runtime;

namespace Rpa.Worker.Execution;

public sealed class WorkItemProcessor(
    RpaWorkerOptions options,
    WorkerEnvironment environment,
    RpaPackageRuntimeRegistry packageRegistry,
    IWorkItemExecutionRepository repository,
    IOneTimeCodeProvider oneTimeCodeProvider,
    ILogger<WorkItemProcessor> logger)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task ProcessAsync(
        RpaWorkItem workItem,
        CancellationToken stoppingToken)
    {
        var executionId = Guid.NewGuid().ToString("N");
        await repository.StartExecutionAsync(executionId, workItem, stoppingToken);
        using var caseTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        caseTimeout.CancelAfter(TimeSpan.FromMinutes(options.CaseTimeoutMinutes));
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            caseTimeout.Token);
        var heartbeatTask = RunHeartbeatAsync(workItem, heartbeatCancellation.Token);
        ConfiguredExecutionGuard? executionGuard = null;
        PlaywrightV2FlowExecutor? v2Executor = null;
        FlowExecutionRequest? executionRequest = null;
        _ = heartbeatTask.ContinueWith(
            _ => caseTimeout.Cancel(),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            if (!options.Definitions.TryGetValue(workItem.RpaCode, out var definition) ||
                !definition.Enabled)
            {
                throw new InvalidOperationException(
                    $"A definição de RPA '{workItem.RpaCode}' não está habilitada.");
            }

            var packageReference = definition.Package
                ?? throw new InvalidOperationException(
                    $"A definição V2 '{workItem.RpaCode}' não possui Package.");
            var packageRpaId = string.IsNullOrWhiteSpace(packageReference.RpaId)
                ? workItem.RpaCode
                : packageReference.RpaId;
            var packageSnapshot = await packageRegistry.ResolveAsync(
                packageRpaId,
                packageReference.OriginName,
                string.IsNullOrWhiteSpace(packageReference.Revision)
                    ? null
                    : new PackageRevision(packageReference.Revision),
                caseTimeout.Token);
            await repository.SetExecutionPackageAsync(
                executionId,
                packageReference.OriginName,
                packageSnapshot,
                caseTimeout.Token);

            var baseConfiguration = await LoadBaseConfigurationAsync(
                definition,
                caseTimeout.Token);
            var configurationDirectory = ResolveConfigurationDirectory(definition);
            var input = ParseObject(workItem.InputJson, "InputJson");
            var configuration = CloneObject(baseConfiguration["Blockly"]?["Variables"]);
            Merge(configuration, ParseObject(workItem.ConfigurationJson, "ConfigurationJson"));
            var attachments = CloneObject(baseConfiguration["Attachments"]);
            Merge(attachments, ParseObject(workItem.AttachmentsJson, "AttachmentsJson"));
            var request = new FlowExecutionRequest(
                executionId,
                input,
                configuration,
                attachments,
                workItem.WorkItemId.ToString("D"),
                workItem.BatchId);
            executionRequest = request;
            FlowInputValidator.Validate(
                packageSnapshot.Flow.Inputs,
                new FlowDataContext(request));

            var runtime = definition.Runtime;
            var statePath = ResolveSessionStatePath(workItem, definition);
            var outputDirectory = Path.Combine(
                environment.Paths.ArtifactRoot,
                SanitizePathSegment(workItem.RpaCode));
            Directory.CreateDirectory(outputDirectory);
            var runtimeOptions = new PlaywrightRuntimeOptions(
                runtime.Headless,
                runtime.Browser,
                runtime.ActionTimeoutSeconds,
                runtime.UploadTimeoutSeconds,
                outputDirectory,
                configurationDirectory,
                runtime.Locale,
                runtime.ViewportWidth,
                runtime.ViewportHeight,
                StorageStatePath: statePath,
                SaveStorageState: runtime.SaveSessionState && statePath is not null,
                ReadinessQuietPeriodMs: runtime.ReadinessQuietPeriodMs,
                FormStabilityMs: runtime.FormStabilityMs,
                BusySelectors: runtime.BusySelectors,
                MaximumArtifactBytes: runtime.MaximumArtifactBytes,
                MaximumArtifactFilesPerExecution:
                    runtime.MaximumArtifactFilesPerExecution,
                ArtifactRetentionDays: runtime.ArtifactRetentionDays);
            PlaywrightRuntimeOptionsValidator.Validate(runtimeOptions);

            executionGuard = new ConfiguredExecutionGuard(
                options.ExecutionMode,
                definition);
            var observer = new DatabaseFlowExecutionObserver(repository);
            var sourceWriter = packageRegistry.ResolveWriter(
                packageRpaId,
                packageReference.OriginName);
            var overlayWriter = packageReference.Overlay is null
                ? null
                : packageRegistry.ResolveWriter(
                    packageRpaId,
                    packageReference.Overlay.OriginName);
            v2Executor = new PlaywrightV2FlowExecutor(
                packageSnapshot,
                runtimeOptions,
                observer: observer,
                executionGuard: executionGuard,
                oneTimeCodeProvider: oneTimeCodeProvider,
                sourceWriteBack: sourceWriter,
                overlayWriteBack: overlayWriter,
                learningFinalization: LocatorLearningFinalizationMode.Deferred);

            var result = await v2Executor.ExecuteAsync(request, caseTimeout.Token);
            var safeValidationBoundaryConfigured =
                options.ExecutionMode == WorkerExecutionMode.SafeValidation &&
                !string.IsNullOrWhiteSpace(definition.SafeValidationBoundaryActionId);
            if (safeValidationBoundaryConfigured &&
                !executionGuard.SafeValidationBoundaryReached)
            {
                throw new InvalidOperationException(
                    $"O fluxo '{workItem.RpaCode}' terminou sem alcançar o limite " +
                    $"seguro '{definition.SafeValidationBoundaryActionId}'.");
            }

            var outputs = MaterializeOutputs(result.Output, definition.Outputs);
            var artifacts = await WorkerArtifactMaterializer.MaterializeAsync(
                result.Output,
                definition.Artifacts,
                environment.Paths.WorkspaceRoot,
                caseTimeout.Token);
            var persistedOutput = SensitiveRuntimeOutputSanitizer.RedactOneTimeCodes(
                result.Output,
                packageSnapshot.Flow);
            await repository.SaveOutputsAsync(
                executionId,
                workItem,
                outputs,
                caseTimeout.Token);
            await repository.SaveArtifactsAsync(
                executionId,
                workItem,
                artifacts,
                caseTimeout.Token);
            await repository.CompleteAsync(
                executionId,
                workItem,
                executionGuard.SafeValidationBoundaryReached
                    ? "Validated"
                    : "Succeeded",
                persistedOutput.ToJsonString(),
                result.ExecutedActions,
                caseTimeout.Token);
            await v2Executor.CompleteLearningAsync(
                request,
                executionGuard.SafeValidationBoundaryReached
                    ? LocatorLearningOutcome.Validated
                    : LocatorLearningOutcome.Succeeded,
                CancellationToken.None);
            if (executionGuard.SafeValidationBoundaryReached)
            {
                logger.LogInformation(
                    "Item {WorkItemId} validado depois do limite seguro {ActionId} " +
                    "da definição {RpaCode}.",
                    workItem.WorkItemId,
                    definition.SafeValidationBoundaryActionId,
                    workItem.RpaCode);
            }
            else
            {
                logger.LogInformation(
                    "Item {WorkItemId} concluído pela definição {RpaCode}.",
                    workItem.WorkItemId,
                    workItem.RpaCode);
            }
        }
        catch (Exception exception)
            when (exception.GetBaseException() is SafeValidationBoundaryException)
        {
            var boundary = (SafeValidationBoundaryException)exception.GetBaseException();
            var output = new JsonObject
            {
                ["safety"] = new JsonObject
                {
                    ["boundaryReached"] = true,
                    ["actionId"] = boundary.ActionId,
                    ["actionName"] = boundary.ActionName
                }
            };
            await repository.CompleteAsync(
                executionId,
                workItem,
                "Validated",
                output.ToJsonString(),
                0,
                CancellationToken.None);
            if (v2Executor is not null && executionRequest is not null)
            {
                await v2Executor.CompleteLearningAsync(
                    executionRequest,
                    LocatorLearningOutcome.Validated,
                    CancellationToken.None);
            }
            logger.LogWarning(
                "Item {WorkItemId} validado com parada segura antes de {ActionId}.",
                workItem.WorkItemId,
                boundary.ActionId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Falha ao processar o item {WorkItemId} da definição {RpaCode}.",
                workItem.WorkItemId,
                workItem.RpaCode);
            var allowRetry = executionGuard?.IrreversibleEffectCompleted != true;
            var outcome = exception.GetBaseException() is OperationCanceledException
                ? LocatorLearningOutcome.Cancelled
                : allowRetry && workItem.AttemptCount < workItem.MaxAttempts
                    ? LocatorLearningOutcome.Retry
                    : LocatorLearningOutcome.Failed;
            try
            {
                await repository.FailAsync(
                    executionId,
                    workItem,
                    exception,
                    allowRetry,
                    CancellationToken.None);
            }
            finally
            {
                if (v2Executor is not null && executionRequest is not null)
                {
                    await v2Executor.CompleteLearningAsync(
                        executionRequest,
                        outcome,
                        CancellationToken.None);
                }
            }
        }
        finally
        {
            heartbeatCancellation.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception heartbeatException)
            {
                logger.LogWarning(
                    heartbeatException,
                    "O heartbeat do item {WorkItemId} terminou com falha.",
                    workItem.WorkItemId);
            }
        }
    }

    private async Task RunHeartbeatAsync(
        RpaWorkItem workItem,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.HeartbeatSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await repository.RenewLeaseAsync(workItem.WorkItemId, cancellationToken);
        }
    }

    private async Task<JsonObject> LoadBaseConfigurationAsync(
        RpaDefinitionOptions definition,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(definition.ConfigurationFile))
        {
            return [];
        }

        var path = RpaWorkerOptionsValidator.ResolvePath(
            environment.Paths.WorkspaceRoot,
            definition.ConfigurationFile);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return JsonNode.Parse(StrictUtf8.GetString(bytes))?.AsObject()
            ?? throw new InvalidOperationException($"A configuração '{path}' está vazia.");
    }

    private string ResolveConfigurationDirectory(RpaDefinitionOptions definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ConfigurationFile))
        {
            return environment.Paths.WorkspaceRoot;
        }

        var path = RpaWorkerOptionsValidator.ResolvePath(
            environment.Paths.WorkspaceRoot,
            definition.ConfigurationFile);
        return Path.GetDirectoryName(path) ?? environment.Paths.WorkspaceRoot;
    }

    private string? ResolveSessionStatePath(
        RpaWorkItem workItem,
        RpaDefinitionOptions definition)
    {
        if (!definition.Runtime.UseSessionState ||
            string.IsNullOrWhiteSpace(workItem.SessionKey))
        {
            return null;
        }

        var identity = $"{workItem.RpaCode}:{workItem.SessionKey}";
        var hash = Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(identity)));
        return Path.Combine(environment.Paths.SessionStateRoot, $"{hash}.json");
    }

    private static IReadOnlyList<MaterializedOutput> MaterializeOutputs(
        JsonObject runtime,
        IReadOnlyList<OutputMappingOptions> mappings)
    {
        var result = new List<MaterializedOutput>();
        foreach (var mapping in mappings)
        {
            if (!RuntimeOutputResolver.TryResolve(runtime, mapping.Source, out var value))
            {
                if (mapping.Required)
                {
                    throw new InvalidOperationException(
                        $"O output obrigatório '{mapping.Name}' não foi produzido em {mapping.Source}.");
                }

                continue;
            }

            result.Add(new MaterializedOutput(
                mapping.Name,
                value?.DeepClone(),
                mapping.Sensitive));
        }

        return result;
    }

    private static JsonObject ParseObject(string json, string fieldName)
    {
        try
        {
            return JsonNode.Parse(json)?.AsObject()
                ?? throw new InvalidOperationException($"{fieldName} está vazio.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"{fieldName} precisa conter um objeto JSON válido.",
                exception);
        }
    }

    private static JsonObject CloneObject(JsonNode? node) =>
        node?.DeepClone() as JsonObject ?? [];

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var property in source)
        {
            target[property.Key] = property.Value?.DeepClone();
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "rpa" : result;
    }
}
