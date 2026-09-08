using System.Text;
using System.Text.Json.Nodes;
using RpaFlow.Packages;
using RpaFlow.Playwright;
using RpaFlow.Playwright.V2;
using RpaFlow.Runtime;

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var configurationPath = Path.GetFullPath(
    ResolveArgument(args, "--config", ResolveDefaultConfigurationPath()));
var configurationDirectory =
    Path.GetDirectoryName(configurationPath) ?? Directory.GetCurrentDirectory();
var configurationBytes = await File.ReadAllBytesAsync(
    configurationPath,
    cancellationSource.Token);
var configuration = JsonNode.Parse(new UTF8Encoding(false, true).GetString(configurationBytes))
    ?.AsObject() ?? throw new InvalidOperationException("A configuração JSON está vazia.");

var runtime = configuration["Runtime"]?.AsObject()
    ?? throw new InvalidOperationException("A seção Runtime é obrigatória.");
var packageStoreRoot = ResolveInsideConfiguration(
    configurationDirectory,
    ResolveArgument(
        args,
        "--package-store",
        runtime["PackageStoreRoot"]?.GetValue<string>() ?? "package-store"));
var rpaId = ResolveArgument(
    args,
    "--rpa-id",
    runtime["RpaId"]?.GetValue<string>() ?? "rpa-exemplo");
var revisionValue = ResolveOptionalArgument(args, "--revision");
var snapshot = await new FileRpaPackageStore(packageStoreRoot).LoadAsync(
    rpaId,
    revisionValue is null ? null : new PackageRevision(revisionValue),
    cancellationSource.Token);
var flow = snapshot.Flow;

var request = new FlowExecutionRequest(
    Guid.NewGuid().ToString("N"),
    CloneObject(configuration["Input"]),
    CloneObject(configuration["Blockly"]?["Variables"]),
    CloneObject(configuration["Attachments"]));
var options = new PlaywrightRuntimeOptions(
    runtime["Headless"]?.GetValue<bool>() ?? true,
    runtime["Browser"]?.GetValue<string>() ?? PlaywrightBrowserSelection.DefaultValue,
    runtime["ActionTimeoutSeconds"]?.GetValue<int>() ?? 30,
    runtime["UploadTimeoutSeconds"]?.GetValue<int>() ?? 90,
    runtime["OutputDirectory"]?.GetValue<string>() ?? "artifacts",
    configurationDirectory,
    Locale: runtime["Locale"]?.GetValue<string>() ?? "pt-BR",
    ViewportWidth: runtime["ViewportWidth"]?.GetValue<int>() ?? 1440,
    ViewportHeight: runtime["ViewportHeight"]?.GetValue<int>() ?? 1000,
    StorageStatePath: runtime["StorageStatePath"]?.GetValue<string>(),
    SaveStorageState: runtime["SaveStorageState"]?.GetValue<bool>() ?? false,
    ReadinessQuietPeriodMs:
        runtime["ReadinessQuietPeriodMs"]?.GetValue<int>() ?? 800,
    FormStabilityMs:
        runtime["FormStabilityMs"]?.GetValue<int>() ?? 600,
    BusySelectors: ReadStringList(runtime["BusySelectors"], "Runtime.BusySelectors"),
    HoldBrowserOpenForInspection:
        runtime["HoldBrowserOpenForInspection"]?.GetValue<bool>() ?? false,
    MaximumArtifactBytes:
        runtime["MaximumArtifactBytes"]?.GetValue<long>() ?? 50 * 1024 * 1024,
    MaximumArtifactFilesPerExecution:
        runtime["MaximumArtifactFilesPerExecution"]?.GetValue<int>() ?? 100,
    ArtifactRetentionDays:
        runtime["ArtifactRetentionDays"]?.GetValue<int>() ?? 30,
    Captcha: ReadCaptcha(runtime["Captcha"]?.AsObject()),
    SpyBrowserHumanize: runtime["SpyBrowserHumanize"]?.GetValue<bool>() ?? true);
PlaywrightRuntimeOptionsValidator.Validate(options);

if (args.Contains("--validate-only", StringComparer.OrdinalIgnoreCase))
{
    FlowInputValidator.Validate(flow.Inputs, new FlowDataContext(request));
    var actionCount = CountStructuralActions(flow.Actions) +
        flow.Subflows.Values.Sum(CountStructuralActions);
    Console.WriteLine(
        actionCount == 1
            ? $"Pacote V2 {snapshot.Revision} válido: 1 ação estrutural."
            : $"Pacote V2 {snapshot.Revision} válido: {actionCount} ações estruturais.");
    return;
}

IFlowExecutor executor = new PlaywrightV2FlowExecutor(snapshot, options);
var result = await executor.ExecuteAsync(request, cancellationSource.Token);
Console.WriteLine(
    $"Execução {result.ExecutionId} concluída com {result.ExecutedActions} ações.");
Console.WriteLine(result.Output.ToJsonString(new() { WriteIndented = true }));

static JsonObject CloneObject(JsonNode? node) =>
    node?.DeepClone() as JsonObject ?? new JsonObject();

static CaptchaOptions? ReadCaptcha(JsonObject? node) =>
    node is null
        ? null
        : new CaptchaOptions(
            ServiceUrl: node["ServiceUrl"]?.GetValue<string>(),
            ServiceApiKey: node["ServiceApiKey"]?.GetValue<string>(),
            OcrModelPath: node["OcrModelPath"]?.GetValue<string>(),
            ServiceTimeoutSeconds: node["ServiceTimeoutSeconds"]?.GetValue<int>() ?? 60,
            RecaptchaMaxAttempts: node["RecaptchaMaxAttempts"]?.GetValue<int>() ?? 3,
            HCaptchaMaxAttempts: node["HCaptchaMaxAttempts"]?.GetValue<int>() ?? 3,
            HumanHandoffTimeoutSeconds:
                node["HumanHandoffTimeoutSeconds"]?.GetValue<int>() ?? 900,
            HumanHandoffPollSeconds:
                node["HumanHandoffPollSeconds"]?.GetValue<int>() ?? 2,
            DeadlineSeconds: node["DeadlineSeconds"]?.GetValue<int>() ?? 90,
            ServiceRetryAttempts: node["ServiceRetryAttempts"]?.GetValue<int>() ?? 2,
            ServiceRetryBackoffMs: node["ServiceRetryBackoffMs"]?.GetValue<int>() ?? 250,
            MaximumServiceResponseBytes:
                node["MaximumServiceResponseBytes"]?.GetValue<int>() ?? 1024 * 1024,
            MaximumImagePixels: node["MaximumImagePixels"]?.GetValue<int>() ?? 16_000_000,
            SliderMinimumScore: node["SliderMinimumScore"]?.GetValue<double>() ?? 0.65,
            LocalOnly: node["LocalOnly"]?.GetValue<bool>() ?? true,
            AutoSolveEnabled: node["AutoSolveEnabled"]?.GetValue<bool>() ?? false,
            AllowVlmFallback: node["AllowVlmFallback"]?.GetValue<bool>() ?? false,
            SamePageWaitSeconds: node["SamePageWaitSeconds"]?.GetValue<int>() ?? 30,
            CloudflareSidecarEnabled:
                node["CloudflareSidecarEnabled"]?.GetValue<bool>() ?? false,
            CloudflareSidecarProvider:
                node["CloudflareSidecarProvider"]?.GetValue<string>() ?? "byparr",
            CloudflareSidecarUrl: node["CloudflareSidecarUrl"]?.GetValue<string>(),
            CloudflareSidecarApiKey: node["CloudflareSidecarApiKey"]?.GetValue<string>(),
            CloudflareSidecarTimeoutSeconds:
                node["CloudflareSidecarTimeoutSeconds"]?.GetValue<int>() ?? 60,
            CloudflareSidecarMaximumResponseBytes:
                node["CloudflareSidecarMaximumResponseBytes"]?.GetValue<int>() ?? 1024 * 1024,
            CloudflareSidecarAllowedHosts: ReadStringList(
                node["CloudflareSidecarAllowedHosts"],
                "Runtime.Captcha.CloudflareSidecarAllowedHosts"));

static IReadOnlyList<string>? ReadStringList(JsonNode? node, string path)
{
    if (node is null)
    {
        return null;
    }

    if (node is not JsonArray array ||
        array.Any(item => item is not JsonValue value ||
            !value.TryGetValue<string>(out _)))
    {
        throw new InvalidOperationException($"{path} deve ser uma lista JSON de textos.");
    }

    return array.Select(item => item!.GetValue<string>()).ToArray();
}

static string ResolveArgument(string[] arguments, string name, string defaultValue)
{
    var index = Array.FindIndex(
        arguments,
        value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    var path = index >= 0
        ? index + 1 < arguments.Length
            ? arguments[index + 1]
            : throw new ArgumentException($"Informe um caminho depois de {name}.")
        : defaultValue;
    return path;
}

static string? ResolveOptionalArgument(string[] arguments, string name)
{
    var index = Array.FindIndex(
        arguments,
        value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index < 0
        ? null
        : index + 1 < arguments.Length
            ? arguments[index + 1]
            : throw new ArgumentException($"Informe um valor depois de {name}.");
}

static int CountStructuralActions(
    IEnumerable<RpaFlow.Contracts.V2.FlowActionDefinition> actions) =>
    actions.Sum(action => 1 +
        CountStructuralActions(action.Actions) +
        CountStructuralActions(action.ElseActions));

static string ResolveDefaultConfigurationPath()
{
    var local = Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");
    return File.Exists(local)
        ? local
        : Path.Combine(AppContext.BaseDirectory, "appsettings.example.json");
}

static string ResolveInsideConfiguration(string directory, string path) =>
    Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(directory, path));
