using System.Text.Json;
using System.Text.Json.Nodes;
using RpaFlow.Contracts.V2;
using RpaFlow.Editor.Configuration;
using RpaFlow.Editor.Services;
using RpaFlow.Packages;
using RpaFlow.Playwright;
using RpaFlow.Playwright.V2;
using RpaFlow.Runtime;

namespace RpaFlow.Editor.AssistedValidation;

public sealed class AssistedExecutionService : IAsyncDisposable
{
    private const int MaximumRetainedExecutions = 10;
    private static readonly HashSet<string> SupportedBrowsers = new(
        ["chromium", "cloakbrowser"],
        StringComparer.OrdinalIgnoreCase);
    private readonly EditorPaths _paths;
    private readonly PackageEditorService _packages;
    private readonly ProjectFileService _files;
    private readonly object _sync = new();
    private readonly Dictionary<string, AssistedExecutionSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public AssistedExecutionService(
        EditorPaths paths,
        PackageEditorService packages,
        ProjectFileService files)
    {
        _paths = paths;
        _packages = packages;
        _files = files;
    }

    public async Task<AssistedExecutionSnapshot> StartAsync(
        AssistedExecutionStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequired(request.ExpectedRevision, "expectedRevision");
        ValidateRequired(request.Browser, "browser");
        ValidateRequired(request.BoundaryActionId, "boundaryActionId");
        if (!SupportedBrowsers.Contains(request.Browser.Trim()))
        {
            throw new InvalidOperationException(
                "O navegador assistido deve ser 'chromium' ou 'cloakbrowser'.");
        }

        var opened = await _packages.OpenAsync(cancellationToken);
        if (!opened.Revision.Equals(request.ExpectedRevision, StringComparison.Ordinal))
        {
            throw new PackageRevisionConflictException(
                $"A revisão aberta '{request.ExpectedRevision}' mudou para " +
                $"'{opened.Revision}'. Recarregue antes de validar.");
        }

        var documents = DeserializeDocuments(request);
        var boundary = FindLeafAction(documents.Flow, request.BoundaryActionId)
            ?? throw new InvalidOperationException(
                "O limite seguro deve apontar para uma ação-folha existente no rascunho.");
        documents.Policy.LocatorResilience.LearningWriteBack =
            LearningWriteBackMode.Disabled;
        documents.Policy.LocatorResilience.Promotion = LocatorPromotionMode.Disabled;
        RpaPackageValidator.Validate(documents);

        var snapshot = new RpaPackageSnapshot(
            _paths.RpaId,
            new PackageRevision(opened.Revision),
            documents,
            new RpaPackageOrigin("editor-assisted-validation", _paths.ProjectRoot));
        var configuration = await ReadConfigurationObjectAsync(cancellationToken);
        var executionId = "homologacao-" + Guid.NewGuid().ToString("N");
        var outputRoot = Path.Combine(
            _paths.ProjectRoot,
            "artifacts",
            "homologacao-editor");
        var session = new AssistedExecutionSession(
            executionId,
            request.Browser.Trim().ToLowerInvariant(),
            boundary.Id,
            boundary.Name,
            opened.Revision,
            snapshot.ContentHash,
            outputRoot);

        lock (_sync)
        {
            if (_sessions.Values.Any(item => item.CanStop))
            {
                throw new InvalidOperationException(
                    "Já existe uma homologação em andamento. Pare-a antes de iniciar outra.");
            }

            TrimCompletedSessions();
            _sessions.Add(session.ExecutionId, session);
        }

        session.RunTask = Task.Run(
            () => RunAsync(session, snapshot, configuration, request),
            CancellationToken.None);
        return session.Snapshot();
    }

    public AssistedExecutionSnapshot Get(string executionId, long afterSequence = 0) =>
        GetSession(executionId).Snapshot(afterSequence);

    public AssistedExecutionSnapshot GetLatest()
    {
        lock (_sync)
        {
            return _sessions.Values
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefault()
                ?.Snapshot()
                ?? throw new KeyNotFoundException(
                    "Nenhuma homologação assistida foi iniciada nesta sessão do editor.");
        }
    }

    public AssistedExecutionSnapshot Stop(string executionId)
    {
        var session = GetSession(executionId);
        session.RequestStop();
        return session.Snapshot();
    }

    public AssistedEvidenceFile GetEvidence(string executionId, string evidenceId) =>
        GetSession(executionId).GetEvidence(evidenceId);

    public async ValueTask DisposeAsync()
    {
        AssistedExecutionSession[] sessions;
        lock (_sync)
        {
            sessions = _sessions.Values.ToArray();
        }

        foreach (var session in sessions)
        {
            session.RequestStop();
        }

        await Task.WhenAll(sessions.Select(item => item.RunTask ?? Task.CompletedTask));
        foreach (var session in sessions)
        {
            session.Dispose();
        }
    }

    private async Task RunAsync(
        AssistedExecutionSession session,
        RpaPackageSnapshot snapshot,
        JsonObject configuration,
        AssistedExecutionStartRequest request)
    {
        var guard = new AssistedValidationGuard(session.BoundaryActionId);
        var observer = new AssistedExecutionObserver(session);
        try
        {
            session.MarkRunning();
            var runtime = GetObject(configuration, "Runtime");
            var configurationDirectory = Path.GetDirectoryName(_paths.ConfigurationFile)
                ?? _paths.ProjectRoot;
            var options = new PlaywrightRuntimeOptions(
                Headless: IsHeadlessTestMode(),
                Browser: session.Browser,
                ActionTimeoutSeconds: ReadInt(runtime, "ActionTimeoutSeconds", 30, 1, 600),
                UploadTimeoutSeconds: ReadInt(runtime, "UploadTimeoutSeconds", 90, 1, 600),
                OutputDirectory: session.OutputRoot,
                ConfigurationDirectory: configurationDirectory,
                Locale: ReadString(runtime, "Locale", "pt-BR"),
                ViewportWidth: ReadInt(runtime, "ViewportWidth", 1440, 320, 7680),
                ViewportHeight: ReadInt(runtime, "ViewportHeight", 1000, 240, 4320),
                StorageStatePath: ReadOptionalString(runtime, "StorageStatePath"),
                SaveStorageState: false,
                ReadinessQuietPeriodMs:
                    ReadInt(runtime, "ReadinessQuietPeriodMs", 800, 0, 60_000),
                FormStabilityMs:
                    ReadInt(runtime, "FormStabilityMs", 600, 0, 60_000),
                BusySelectors: ReadStringList(runtime, "BusySelectors"),
                HoldBrowserOpenForInspection: false,
                MaximumArtifactBytes: 50 * 1024 * 1024,
                MaximumArtifactFilesPerExecution: 250,
                ArtifactRetentionDays: 7,
                CaptureScreenshotsAfterActions: request.CaptureScreenshots);
            var executionRequest = new FlowExecutionRequest(
                session.ExecutionId,
                CloneObject(configuration, "Input"),
                CloneObject(GetObject(configuration, "Blockly"), "Variables"),
                CloneObject(configuration, "Attachments"));
            var result = await new PlaywrightV2FlowExecutor(
                    snapshot,
                    options,
                    observer,
                    guard)
                .ExecuteAsync(executionRequest, session.CancellationToken);

            if (!guard.BoundaryReached)
            {
                session.MarkFailed(
                    "O roteiro terminou sem alcançar a última etapa segura escolhida.",
                    result.ExecutedActions);
                return;
            }

            session.MarkValidated(result.ExecutedActions);
        }
        catch (OperationCanceledException)
        {
            session.MarkCancelled();
        }
        catch (FlowExecutionException exception)
        {
            session.MarkFailed(DescribeFailure(exception.Failure), session.ExecutedActions);
        }
        catch (Exception exception)
        {
            session.MarkFailed(exception.Message, session.ExecutedActions);
        }
    }

    private AssistedExecutionSession GetSession(string executionId)
    {
        lock (_sync)
        {
            return _sessions.GetValueOrDefault(executionId)
                ?? throw new KeyNotFoundException("Homologação assistida não encontrada.");
        }
    }

    private void TrimCompletedSessions()
    {
        var removable = _sessions.Values
            .Where(item => !item.CanStop)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Skip(MaximumRetainedExecutions - 1)
            .ToArray();
        foreach (var item in removable)
        {
            _sessions.Remove(item.ExecutionId);
            item.Dispose();
        }
    }

    private static RpaPackageDocuments DeserializeDocuments(
        AssistedExecutionStartRequest request) =>
        new(
            Deserialize<FlowDefinition>(request.Flow, "flow.production.json"),
            Deserialize<LocatorCatalog>(request.Locators, "locators.production.json"),
            Deserialize<RpaPolicyDefinition>(request.Policy, "rpa.policy.json"));

    private static T Deserialize<T>(JsonElement value, string description)
        where T : class =>
        V2JsonSerializer.Deserialize<T>(value.GetRawText(), description);

    private async Task<JsonObject> ReadConfigurationObjectAsync(
        CancellationToken cancellationToken)
    {
        var document = await _files.ReadConfigurationAsync(cancellationToken);
        return JsonNode.Parse(document.GetRawText())?.AsObject()
            ?? throw new InvalidOperationException("A configuração local está vazia.");
    }

    private static FlowActionDefinition? FindLeafAction(
        FlowDefinition flow,
        string actionId)
    {
        foreach (var action in Enumerate(flow.Actions)
                     .Concat(flow.Subflows.Values.SelectMany(Enumerate)))
        {
            if (action.Id.Equals(actionId.Trim(), StringComparison.OrdinalIgnoreCase) &&
                action.Actions.Count == 0 &&
                action.ElseActions.Count == 0)
            {
                return action;
            }
        }

        return null;
    }

    private static IEnumerable<FlowActionDefinition> Enumerate(
        IEnumerable<FlowActionDefinition> actions)
    {
        foreach (var action in actions)
        {
            yield return action;
            foreach (var child in Enumerate(action.Actions)) yield return child;
            foreach (var child in Enumerate(action.ElseActions)) yield return child;
        }
    }

    private static JsonObject GetObject(JsonObject owner, string name) =>
        GetNode(owner, name) as JsonObject ?? new JsonObject();

    private static JsonObject CloneObject(JsonObject owner, string name) =>
        GetNode(owner, name)?.DeepClone() as JsonObject ?? new JsonObject();

    private static JsonNode? GetNode(JsonObject owner, string name)
    {
        var key = owner.Select(item => item.Key).FirstOrDefault(
            key => key.Equals(name, StringComparison.OrdinalIgnoreCase));
        return key is null ? null : owner[key];
    }

    private static int ReadInt(
        JsonObject owner,
        string name,
        int fallback,
        int minimum,
        int maximum)
    {
        var value = GetNode(owner, name)?.GetValue<int>() ?? fallback;
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"Runtime.{name} deve ficar entre {minimum} e {maximum}.");
        }
        return value;
    }

    private static string ReadString(JsonObject owner, string name, string fallback) =>
        ReadOptionalString(owner, name) ?? fallback;

    private static string? ReadOptionalString(JsonObject owner, string name)
    {
        var value = GetNode(owner, name)?.GetValue<string>()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IReadOnlyList<string>? ReadStringList(JsonObject owner, string name)
    {
        var node = GetNode(owner, name);
        if (node is null) return null;
        if (node is not JsonArray array ||
            array.Any(item => item is not JsonValue value ||
                !value.TryGetValue<string>(out _)))
        {
            throw new InvalidOperationException($"Runtime.{name} deve ser uma lista de textos.");
        }
        return array.Select(item => item!.GetValue<string>()).ToArray();
    }

    private static bool IsHeadlessTestMode() =>
        Environment.GetEnvironmentVariable("RPABLOCKLY_ASSISTED_HEADLESS") == "1";

    private static string DescribeFailure(FlowExecutionFailure failure)
    {
        var location = string.IsNullOrWhiteSpace(failure.ActionName)
            ? "antes da primeira etapa"
            : $"na etapa '{failure.ActionName}'";
        return $"A homologação falhou {location} ({failure.Category}). " +
            "Revise o card destacado e as evidências visuais.";
    }

    private static void ValidateRequired(string? value, string property)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{property} é obrigatório.");
        }
    }
}

internal sealed class AssistedExecutionObserver(AssistedExecutionSession session) :
    IFlowExecutionObserver
{
    public ValueTask ObserveAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        session.Observe(executionEvent);
        return ValueTask.CompletedTask;
    }
}

internal sealed class AssistedExecutionSession : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<AssistedExecutionEvent> _events = [];
    private readonly Dictionary<string, StoredEvidence> _evidence =
        new(StringComparer.OrdinalIgnoreCase);
    private long _nextSequence;
    private string _status = "starting";
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private int _executedActions;
    private bool _boundaryReached;
    private string? _error;

    public AssistedExecutionSession(
        string executionId,
        string browser,
        string boundaryActionId,
        string boundaryActionName,
        string sourceRevision,
        string draftHash,
        string outputRoot)
    {
        ExecutionId = executionId;
        Browser = browser;
        BoundaryActionId = boundaryActionId;
        BoundaryActionName = boundaryActionName;
        SourceRevision = sourceRevision;
        DraftHash = draftHash;
        OutputRoot = Path.GetFullPath(outputRoot);
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string ExecutionId { get; }
    public string Browser { get; }
    public string BoundaryActionId { get; }
    public string BoundaryActionName { get; }
    public string SourceRevision { get; }
    public string DraftHash { get; }
    public string OutputRoot { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public CancellationToken CancellationToken => _cancellation.Token;
    public Task? RunTask { get; set; }

    public bool CanStop
    {
        get
        {
            lock (_sync)
            {
                return _status is "starting" or "running" or "stopping";
            }
        }
    }

    public int ExecutedActions
    {
        get
        {
            lock (_sync) return _executedActions;
        }
    }

    public void MarkRunning()
    {
        lock (_sync)
        {
            _status = "running";
            _startedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void MarkValidated(int executedActions)
    {
        lock (_sync)
        {
            _status = "validated";
            _boundaryReached = true;
            _executedActions = Math.Max(_executedActions, executedActions);
            _completedAtUtc = DateTimeOffset.UtcNow;
            AddEventUnsafe("validationBoundaryReached", detail: BoundaryActionName);
        }
    }

    public void MarkCancelled()
    {
        lock (_sync)
        {
            _status = "cancelled";
            _completedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void MarkFailed(string error, int executedActions)
    {
        lock (_sync)
        {
            _status = "failed";
            _error = SanitizeDetail(error, "A homologação falhou.");
            _executedActions = Math.Max(_executedActions, executedActions);
            _completedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void RequestStop()
    {
        lock (_sync)
        {
            if (_status is not ("starting" or "running" or "stopping")) return;
            _status = "stopping";
        }
        _cancellation.Cancel();
    }

    public void Observe(FlowExecutionEvent value)
    {
        lock (_sync)
        {
            _executedActions = Math.Max(_executedActions, value.ExecutedActions ?? 0);
            string? evidenceId = null;
            string? detail = value.Detail;
            if (value.Kind is "actionEvidenceCaptured" or "failureEvidenceCaptured")
            {
                evidenceId = RegisterEvidenceUnsafe(value);
                detail = evidenceId is null
                    ? "A captura não pôde ser registrada."
                    : "Captura visual salva.";
            }
            else if (value.Kind == "actionEvidenceFailed")
            {
                detail = "A captura visual desta etapa falhou sem interromper o roteiro.";
            }
            else
            {
                detail = SanitizeDetail(detail, null);
            }

            _events.Add(new AssistedExecutionEvent(
                ++_nextSequence,
                value.Kind,
                value.OccurredAtUtc,
                value.ActionId,
                value.ActionName,
                value.ActionType,
                value.ExecutedActions,
                value.ElapsedMilliseconds,
                value.FailureCategory?.ToString(),
                value.Retryable,
                evidenceId,
                detail));
        }
    }

    public AssistedExecutionSnapshot Snapshot(long afterSequence = 0)
    {
        lock (_sync)
        {
            return new AssistedExecutionSnapshot(
                ExecutionId,
                _status,
                Browser,
                BoundaryActionId,
                BoundaryActionName,
                SourceRevision,
                DraftHash,
                CreatedAtUtc,
                _startedAtUtc,
                _completedAtUtc,
                _executedActions,
                _boundaryReached,
                _status is "starting" or "running" or "stopping",
                _error,
                _events.Where(item => item.Sequence > afterSequence).ToArray(),
                _evidence.Values
                    .OrderBy(item => item.Public.CapturedAtUtc)
                    .Select(item => item.Public)
                    .ToArray());
        }
    }

    public AssistedEvidenceFile GetEvidence(string evidenceId)
    {
        lock (_sync)
        {
            var stored = _evidence.GetValueOrDefault(evidenceId)
                ?? throw new KeyNotFoundException("Evidência da homologação não encontrada.");
            if (!File.Exists(stored.Path))
            {
                throw new KeyNotFoundException("O arquivo da evidência não está mais disponível.");
            }
            return new AssistedEvidenceFile(
                stored.Path,
                stored.Public.FileName,
                ContentType(stored.Path));
        }
    }

    public void Dispose() => _cancellation.Dispose();

    private string? RegisterEvidenceUnsafe(FlowExecutionEvent value)
    {
        if (string.IsNullOrWhiteSpace(value.Detail)) return null;
        var path = Path.GetFullPath(value.Detail);
        var relative = Path.GetRelativePath(OutputRoot, path);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(path) ||
            !IsImage(path))
        {
            return null;
        }

        var id = Guid.NewGuid().ToString("N");
        var kind = value.Kind == "failureEvidenceCaptured" ? "failure" : "step";
        _evidence.Add(id, new StoredEvidence(
            path,
            new AssistedExecutionEvidence(
                id,
                kind,
                Path.GetFileName(path),
                value.OccurredAtUtc,
                value.ActionId,
                value.ActionName)));
        return id;
    }

    private void AddEventUnsafe(string kind, string? detail = null) =>
        _events.Add(new AssistedExecutionEvent(
            ++_nextSequence,
            kind,
            DateTimeOffset.UtcNow,
            BoundaryActionId,
            BoundaryActionName,
            Detail: detail));

    private static bool IsImage(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static string ContentType(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

    private static string? SanitizeDetail(string? value, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var text = value.ReplaceLineEndings(" ").Trim();
        return text.Length <= 500 ? text : text[..500] + "…";
    }

    private sealed record StoredEvidence(string Path, AssistedExecutionEvidence Public);
}
