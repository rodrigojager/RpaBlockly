using System.Text;
using System.Text.Json.Nodes;
using RpaFlow.Contracts;
using RpaFlow.Playwright;
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
var flowPath = ResolveInsideConfiguration(
    configurationDirectory,
    ResolveArgument(
        args,
        "--flow",
        runtime["FlowFile"]?.GetValue<string>() ?? "flow.production.json"));
var flow = await new JsonFlowLoader().LoadAsync(flowPath, cancellationSource.Token);

var request = new FlowExecutionRequest(
    Guid.NewGuid().ToString("N"),
    CloneObject(configuration["Input"]),
    CloneObject(configuration["Blockly"]?["Variables"]),
    CloneObject(configuration["Attachments"]));
var options = new PlaywrightRuntimeOptions(
    runtime["Headless"]?.GetValue<bool>() ?? true,
    runtime["Browser"]?.GetValue<string>() ?? "chromium",
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
        runtime["HoldBrowserOpenForInspection"]?.GetValue<bool>() ?? false);
PlaywrightRuntimeOptionsValidator.Validate(options);

if (args.Contains("--validate-only", StringComparer.OrdinalIgnoreCase))
{
    FlowInputValidator.Validate(flow.Inputs, new FlowDataContext(request));
    var actionCount = FlowDefinitionMetrics.CountStructuralActions(flow);
    Console.WriteLine(
        actionCount == 1
            ? "Fluxo válido: 1 ação estrutural."
            : $"Fluxo válido: {actionCount} ações estruturais.");
    return;
}

IFlowExecutor executor = new PlaywrightFlowExecutor(flow, options);
var result = await executor.ExecuteAsync(request, cancellationSource.Token);
Console.WriteLine(
    $"Execução {result.ExecutionId} concluída com {result.ExecutedActions} ações.");
Console.WriteLine(result.Output.ToJsonString(new() { WriteIndented = true }));

static JsonObject CloneObject(JsonNode? node) =>
    node?.DeepClone() as JsonObject ?? new JsonObject();

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

static string ResolveDefaultConfigurationPath()
{
    var local = Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");
    return File.Exists(local)
        ? local
        : Path.Combine(AppContext.BaseDirectory, "appsettings.example.json");
}

static string ResolveInsideConfiguration(string directory, string path) =>
    Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(directory, path));
