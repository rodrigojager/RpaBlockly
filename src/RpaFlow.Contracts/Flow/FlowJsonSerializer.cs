using System.Text.Json;
using System.Text.Json.Serialization;

namespace RpaFlow.Contracts;

public static class FlowJsonSerializer
{
    private static readonly JsonSerializerOptions StrictReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static FlowDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<FlowDefinition>(json, StrictReadOptions)
        ?? throw new InvalidOperationException(
            "O arquivo de fluxo está vazio ou possui JSON inválido.");

    public static FlowDefinition Deserialize(JsonElement root) =>
        root.Deserialize<FlowDefinition>(StrictReadOptions)
        ?? throw new InvalidOperationException("O fluxo JSON está vazio.");
}
