using System.Text.Json;
using System.Text.Json.Serialization;

namespace RpaFlow.Contracts.V2;

public sealed class FlowDefinition
{
    public int SchemaVersion { get; set; } = 2;

    public string Name { get; set; } = string.Empty;

    public List<FlowInputRequirementDefinition> Inputs { get; set; } = [];

    public List<FlowActionDefinition> Actions { get; set; } = [];

    public Dictionary<string, List<FlowActionDefinition>> Subflows { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FlowActionDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public LocatorUseDefinition? Target { get; set; }

    public LocatorUseDefinition? Trigger { get; set; }

    public LocatorUseDefinition? Options { get; set; }

    public LocatorUseDefinition? Ready { get; set; }

    public LocatorUseDefinition? Success { get; set; }

    public LocatorUseDefinition? Protocol { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Value { get; set; }

    public string? ValueSource { get; set; }

    public string? ProviderAlias { get; set; }

    public string? NotBeforeSource { get; set; }

    /// <summary>
    /// Destino de dados em runtime.*. Na V2, target é reservado ao localizador.
    /// </summary>
    public string? Output { get; set; }

    public string? Property { get; set; }

    public string? Attribute { get; set; }

    public string? State { get; set; }

    public string? Comparison { get; set; }

    public string? Operation { get; set; }

    public string? OptionMode { get; set; }

    public string? SuccessText { get; set; }

    public string? ProtocolPattern { get; set; }

    public string? CompletionTarget { get; set; }

    public string? ConfirmationMessageTarget { get; set; }

    public string? ProtocolTarget { get; set; }

    public string? ScreenshotName { get; set; }

    public string? DestinationDirectory { get; set; }

    public string? DestinationDirectorySource { get; set; }

    public string? FileName { get; set; }

    public string? FileNameSource { get; set; }

    public bool? SeparateByExecution { get; set; }

    public string? ConflictStrategy { get; set; }

    public string? DownloadMode { get; set; }

    public string? Method { get; set; }

    public string? BodyType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement RequestBody { get; set; }

    public string? RequestBodySource { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement RequestHeaders { get; set; }

    public string? RequestHeadersSource { get; set; }

    public int? TimeoutMs { get; set; }

    public int? PollIntervalMs { get; set; }

    public int? DelayMs { get; set; }

    public int? DecimalPlaces { get; set; }

    public int? MaxItems { get; set; }

    public string? CommitKey { get; set; }

    public bool ClearFirst { get; set; }

    public bool BlurAfter { get; set; }

    public bool Optional { get; set; }

    public FlowConditionDefinition? Condition { get; set; }

    public List<FlowActionDefinition> Actions { get; set; } = [];

    public List<FlowActionDefinition> ElseActions { get; set; } = [];

    public int? Times { get; set; }

    public string? TimesSource { get; set; }

    public List<JsonElement>? Items { get; set; }

    public string? ItemsSource { get; set; }

    public string? ItemVariable { get; set; }

    public string? IndexVariable { get; set; }

    public string? Subflow { get; set; }
}

public sealed class FlowConditionDefinition
{
    public string Type { get; set; } = string.Empty;

    public string? Operator { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement LeftValue { get; set; }

    public string? LeftSource { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement RightValue { get; set; }

    public string? RightSource { get; set; }

    public bool IgnoreCase { get; set; }

    public LocatorUseDefinition? Locator { get; set; }

    public string? State { get; set; }
}

public sealed class FlowInputRequirementDefinition
{
    public string Path { get; set; } = string.Empty;

    public string Type { get; set; } = "any";

    public bool Required { get; set; } = true;
}

public sealed class LocatorUseDefinition
{
    public string LocatorId { get; set; } = string.Empty;

    public LocatorCardinality Cardinality { get; set; } = LocatorCardinality.Single;
}

public enum LocatorCardinality
{
    Single,
    First,
    Many
}
