using System.Globalization;
using System.Text.Json.Nodes;
using RpaFlow.Contracts.V2;

namespace RpaFlow.Runtime.V2;

public static class V2FlowValueResolver
{
    public static string ResolveRequired(
        FlowActionDefinition action,
        FlowDataContext data) =>
        FlowValueResolver.ResolveRequired(
            action.Value,
            action.ValueSource,
            $"a ação '{action.Name}'",
            data);

    public static string? ResolveOptional(
        FlowActionDefinition action,
        FlowDataContext data) =>
        FlowValueResolver.ResolveOptional(action.Value, action.ValueSource, data);

    public static JsonNode? ResolveNode(
        FlowActionDefinition action,
        FlowDataContext data)
    {
        if (FlowValueResolver.HasLiteral(action.Value))
        {
            return FlowValueResolver.ToNode(action.Value);
        }

        if (string.IsNullOrWhiteSpace(action.ValueSource))
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' não informou value nem valueSource.");
        }

        return data.ResolveRequired(action.ValueSource, $"A ação '{action.Name}'");
    }

    public static IReadOnlyList<JsonNode?> ResolveList(
        FlowActionDefinition action,
        FlowDataContext data)
    {
        if (action.Items is not null)
        {
            return action.Items.Select(FlowValueResolver.ToNode).ToArray();
        }

        var source = action.ItemsSource!;
        var resolved = data.ResolveRequired(source, $"A lista de '{action.Name}'");
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
        var rawValue = FlowValueResolver.ConvertSimpleValue(
            node,
            action.TimesSource!) ?? string.Empty;
        if (!int.TryParse(
                rawValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var count) ||
            count is < 0 or > FlowDefinitionValidator.MaximumLoopIterations)
        {
            throw new InvalidOperationException(
                $"A repetição '{action.Name}' exige um inteiro entre 0 e " +
                $"{FlowDefinitionValidator.MaximumLoopIterations}; recebido '{rawValue}'.");
        }

        return count;
    }
}
