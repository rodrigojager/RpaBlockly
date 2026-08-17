using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RpaFlow.Contracts.V2;
using RpaFlow.Runtime;
using FlowConditionDefinition = RpaFlow.Contracts.V2.FlowConditionDefinition;

namespace RpaFlow.Playwright.V2;

internal static class V2ConditionEvaluator
{
    public static async Task<bool> EvaluateAsync(
        FlowConditionDefinition condition,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        if (condition.Type.Equals("element", StringComparison.OrdinalIgnoreCase))
        {
            var use = condition.Locator ?? throw new InvalidOperationException(
                "A condição de elemento não informou locator.");
            var resolution = await execution.ResolveAsync(
                use,
                LocatorRequiredState.Any,
                cancellationToken,
                allowEmpty: true);
            return await FlowLocatorState.EvaluateAsync(
                resolution.Locator,
                condition.State!,
                use.Cardinality == LocatorCardinality.Single ? "single" : null,
                "condição de elemento");
        }

        var left = FlowValueResolver.ResolveNodeOptional(
            condition.LeftValue,
            condition.LeftSource,
            execution.Context.Data);
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
            execution.Context.Data);
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

    private static bool ValuesAreEqual(JsonNode? left, JsonNode? right, bool ignoreCase)
    {
        if (ignoreCase && left is JsonValue && right is JsonValue)
        {
            return string.Equals(
                FlowValueResolver.ConvertSimpleValue(left, "leftValue"),
                FlowValueResolver.ConvertSimpleValue(right, "rightValue"),
                StringComparison.OrdinalIgnoreCase);
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
}
