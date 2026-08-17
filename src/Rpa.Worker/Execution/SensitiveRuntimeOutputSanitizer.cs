using System.Text.Json.Nodes;
using RpaFlow.Contracts.V2;

namespace Rpa.Worker.Execution;

internal static class SensitiveRuntimeOutputSanitizer
{
    public static JsonObject RedactOneTimeCodes(
        JsonObject output,
        FlowDefinition flow)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(flow);
        var sanitized = output.DeepClone().AsObject();
        foreach (var target in EnumerateOneTimeCodeTargets(flow))
        {
            RemoveRuntimePath(sanitized, target);
        }

        return sanitized;
    }

    public static IReadOnlySet<string> EnumerateOneTimeCodeTargets(
        FlowDefinition flow) =>
        EnumerateActions(flow)
            .Where(action => action.Type.Equals(
                "waitForOneTimeCode",
                StringComparison.OrdinalIgnoreCase))
            .Select(action => action.Output?.Trim())
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool PathsOverlap(string first, string second)
    {
        var left = first.Trim().TrimEnd('.');
        var right = second.Trim().TrimEnd('.');
        return left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
               left.StartsWith(right + ".", StringComparison.OrdinalIgnoreCase) ||
               right.StartsWith(left + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveRuntimePath(JsonObject output, string target)
    {
        const string prefix = "runtime.";
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var segments = target[prefix.Length..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return;
        }

        var current = output;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var key = FindKey(current, segments[index]);
            if (key is null || current[key] is not JsonObject child)
            {
                return;
            }

            current = child;
        }

        var finalKey = FindKey(current, segments[^1]);
        if (finalKey is not null)
        {
            current.Remove(finalKey);
        }
    }

    private static string? FindKey(JsonObject value, string expected) =>
        value.Select(property => property.Key).FirstOrDefault(key =>
            key.Equals(expected, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<FlowActionDefinition> EnumerateActions(
        FlowDefinition flow)
    {
        var pending = new Stack<FlowActionDefinition>(
            flow.Actions.Concat(flow.Subflows.Values.SelectMany(actions => actions)));
        while (pending.TryPop(out var action))
        {
            yield return action;
            foreach (var nested in action.Actions.Concat(action.ElseActions))
            {
                pending.Push(nested);
            }
        }
    }
}
