using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowActionDefinition = RpaFlow.Contracts.V2.FlowActionDefinition;
using FlowDefinitionValidator = RpaFlow.Contracts.V2.FlowDefinitionValidator;

namespace RpaFlow.Runtime;

public static class FlowValueResolver
{
    public static bool HasLiteral(JsonElement value) =>
        value.ValueKind != JsonValueKind.Undefined;

    public static string ResolveRequired(
        FlowActionDefinition action,
        FlowDataContext data) =>
        ResolveRequired(
            action.Value,
            action.ValueSource,
            $"a ação '{action.Name}'",
            data);

    public static string? ResolveOptional(
        FlowActionDefinition action,
        FlowDataContext data) =>
        ResolveOptional(action.Value, action.ValueSource, data);

    public static string ResolveRequired(
        JsonElement value,
        string? valueSource,
        string description,
        FlowDataContext data) =>
        ResolveOptional(value, valueSource, data)
            ?? throw new InvalidOperationException(
                $"{description} não encontrou um valor simples em '{valueSource}'.");

    public static string? ResolveOptional(
        JsonElement value,
        string? valueSource,
        FlowDataContext data)
    {
        var node = ResolveNodeOptional(value, valueSource, data);
        return ConvertSimpleValue(node, valueSource ?? "valor literal");
    }

    public static JsonNode? ResolveNode(
        FlowActionDefinition action,
        FlowDataContext data)
    {
        if (HasLiteral(action.Value))
        {
            return ToNode(action.Value);
        }

        if (string.IsNullOrWhiteSpace(action.ValueSource))
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' não informou value nem valueSource.");
        }

        return data.ResolveRequired(
            action.ValueSource,
            $"A ação '{action.Name}'");
    }

    public static JsonNode? ResolveNodeOptional(
        JsonElement value,
        string? valueSource,
        FlowDataContext data)
    {
        if (HasLiteral(value))
        {
            return ToNode(value);
        }

        if (string.IsNullOrWhiteSpace(valueSource))
        {
            return null;
        }

        if (!data.TryResolve(valueSource, out var resolved))
        {
            throw new InvalidOperationException(
                $"Caminho de dados não encontrado: '{valueSource}'.");
        }

        return resolved;
    }

    public static string? ResolveOptionalText(
        string? literal,
        string? source,
        FlowDataContext data)
    {
        if (literal is not null)
        {
            return literal;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var value = data.ResolveRequired(source, "A resolução de texto");
        return ConvertSimpleValue(value, source);
    }

    public static IReadOnlyList<JsonNode?> ResolveList(
        FlowActionDefinition action,
        FlowDataContext data)
    {
        if (action.Items is not null)
        {
            return action.Items.Select(ToNode).ToArray();
        }

        var source = action.ItemsSource!;
        var resolved = data.ResolveRequired(
            source,
            $"A lista de '{action.Name}'");
        if (resolved is not JsonArray array)
        {
            throw new InvalidOperationException(
                $"O caminho '{source}' da ação '{action.Name}' deve conter uma lista JSON.");
        }

        return array.ToArray();
    }

    public static int ResolveIterationCount(
        FlowActionDefinition action,
        FlowDataContext data)
    {
        if (action.Times is not null)
        {
            return action.Times.Value;
        }

        var node = data.ResolveRequired(
            action.TimesSource!,
            $"A repetição '{action.Name}'");
        var rawValue = ConvertSimpleValue(node, action.TimesSource!) ?? string.Empty;
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            count is < 0 or > FlowDefinitionValidator.MaximumLoopIterations)
        {
            throw new InvalidOperationException(
                $"A repetição '{action.Name}' exige um inteiro entre 0 e " +
                $"{FlowDefinitionValidator.MaximumLoopIterations}; recebido '{rawValue}'.");
        }

        return count;
    }

    public static string? ConvertSimpleValue(JsonNode? value, string source)
    {
        if (value is null)
        {
            return null;
        }

        if (value is not JsonValue scalar)
        {
            throw new InvalidOperationException(
                $"O caminho '{source}' contém objeto ou lista e não pode ser usado como valor simples.");
        }

        if (scalar.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (scalar.TryGetValue<bool>(out var boolean))
        {
            return boolean ? "true" : "false";
        }

        return scalar.ToJsonString();
    }

    public static JsonNode? ToNode(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Undefined => throw new InvalidOperationException(
                "Não é possível converter um valor JSON indefinido."),
            JsonValueKind.Null => null,
            _ => JsonNode.Parse(value.GetRawText())
        };
}
