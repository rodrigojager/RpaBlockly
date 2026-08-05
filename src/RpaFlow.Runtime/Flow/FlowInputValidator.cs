using System.Text.Json.Nodes;
using RpaFlow.Contracts;

namespace RpaFlow.Runtime;

public static class FlowInputValidator
{
    private static readonly HashSet<string> SupportedTypes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "any", "string", "number", "boolean", "object", "array", "null"
    };

    public static void Validate(
        IReadOnlyList<FlowInputRequirementDefinition> requirements,
        FlowDataContext data)
    {
        var errors = new List<string>();
        foreach (var requirement in requirements)
        {
            if (!data.TryResolve(requirement.Path, out var value))
            {
                if (requirement.Required)
                {
                    errors.Add($"Entrada obrigatória ausente: '{requirement.Path}'.");
                }

                continue;
            }

            if (!MatchesType(value, requirement.Type))
            {
                errors.Add(
                    $"A entrada '{requirement.Path}' deveria ser {requirement.Type}, " +
                    $"mas recebeu {DescribeType(value)}.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Dados de entrada incompatíveis com o fluxo:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
        }
    }

    private static bool MatchesType(JsonNode? value, string type) =>
        type.ToLowerInvariant() switch
        {
            "any" => true,
            "null" => value is null,
            "object" => value is JsonObject,
            "array" => value is JsonArray,
            "string" => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out _),
            "number" => IsNumber(value),
            "boolean" => value is JsonValue booleanValue &&
                booleanValue.TryGetValue<bool>(out _),
            _ => false
        };

    private static bool IsNumber(JsonNode? value) =>
        value is JsonValue jsonValue &&
        (jsonValue.TryGetValue<int>(out _) ||
            jsonValue.TryGetValue<long>(out _) ||
            jsonValue.TryGetValue<decimal>(out _) ||
            jsonValue.TryGetValue<double>(out _));

    private static string DescribeType(JsonNode? value) =>
        value switch
        {
            null => "null",
            JsonObject => "object",
            JsonArray => "array",
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out _) => "string",
            JsonValue jsonValue when jsonValue.TryGetValue<bool>(out _) => "boolean",
            _ when IsNumber(value) => "number",
            _ => "valor desconhecido"
        };
}
