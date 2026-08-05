using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public static class FlowConditionEvaluator
{
    public static async Task<bool> EvaluateAsync(
        FlowConditionDefinition condition,
        RpaContext context)
    {
        if (string.Equals(
            condition.Type,
            "element",
            StringComparison.OrdinalIgnoreCase))
        {
            return await EvaluateElementAsync(condition, context);
        }

        var left = FlowValueResolver.ResolveNodeOptional(
            condition.LeftValue,
            condition.LeftSource,
            context.Data);
        var operatorName = condition.Operator ??
            throw new InvalidOperationException("Operador condicional não informado.");
        if (operatorName.Equals("isEmpty", StringComparison.OrdinalIgnoreCase))
        {
            return IsEmpty(left);
        }

        if (operatorName.Equals("isNotEmpty", StringComparison.OrdinalIgnoreCase))
        {
            return !IsEmpty(left);
        }

        var right = FlowValueResolver.ResolveNodeOptional(
            condition.RightValue,
            condition.RightSource,
            context.Data);
        if (operatorName.Equals("equals", StringComparison.OrdinalIgnoreCase))
        {
            return ValuesAreEqual(left, right, condition.IgnoreCase);
        }

        if (operatorName.Equals("notEquals", StringComparison.OrdinalIgnoreCase))
        {
            return !ValuesAreEqual(left, right, condition.IgnoreCase);
        }

        if (left is JsonArray array &&
            operatorName.Equals("contains", StringComparison.OrdinalIgnoreCase))
        {
            return array.Any(item => ValuesAreEqual(item, right, condition.IgnoreCase));
        }

        if (left is JsonArray notContainsArray &&
            operatorName.Equals("notContains", StringComparison.OrdinalIgnoreCase))
        {
            return notContainsArray.All(item =>
                !ValuesAreEqual(item, right, condition.IgnoreCase));
        }

        var leftText = FlowValueResolver.ConvertSimpleValue(
            left,
            condition.LeftSource ?? "leftValue") ?? string.Empty;
        var rightText = FlowValueResolver.ConvertSimpleValue(
            right,
            condition.RightSource ?? "rightValue") ?? string.Empty;
        var comparison = condition.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return operatorName.ToLowerInvariant() switch
        {
            "contains" => leftText.Contains(rightText, comparison),
            "notcontains" => !leftText.Contains(rightText, comparison),
            "startswith" => leftText.StartsWith(rightText, comparison),
            "endswith" => leftText.EndsWith(rightText, comparison),
            "matchesregex" => Regex.IsMatch(
                leftText,
                rightText,
                condition.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None,
                TimeSpan.FromSeconds(1)),
            _ => throw new InvalidOperationException(
                $"Operador condicional não interpretado: '{operatorName}'.")
        };
    }

    private static bool ValuesAreEqual(
        JsonNode? left,
        JsonNode? right,
        bool ignoreCase)
    {
        if (ignoreCase && left is JsonValue && right is JsonValue)
        {
            var leftText = FlowValueResolver.ConvertSimpleValue(left, "leftValue");
            var rightText = FlowValueResolver.ConvertSimpleValue(right, "rightValue");
            return string.Equals(leftText, rightText, StringComparison.OrdinalIgnoreCase);
        }

        return JsonNode.DeepEquals(left, right);
    }

    private static bool IsEmpty(JsonNode? value) =>
        value switch
        {
            null => true,
            JsonArray array => array.Count == 0,
            JsonObject objectValue => objectValue.Count == 0,
            JsonValue scalar when scalar.TryGetValue<string>(out var text) =>
                string.IsNullOrEmpty(text),
            _ => false
        };

    private static async Task<bool> EvaluateElementAsync(
        FlowConditionDefinition condition,
        RpaContext context)
    {
        var locator = FlowLocatorFactory.Create(
            context.Page,
            condition,
            context.Data);
        return await FlowLocatorState.EvaluateAsync(
            locator,
            condition.State!,
            condition.MatchMode,
            "condição de elemento");
    }
}
