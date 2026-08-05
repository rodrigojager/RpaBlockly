using System.Globalization;
using System.Text.Json.Nodes;

namespace Rpa.Worker.Execution;

public static class RuntimeOutputResolver
{
    public static bool TryResolve(
        JsonObject runtime,
        string source,
        out JsonNode? value)
    {
        value = runtime;
        if (!source.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var rawSegment in source["runtime.".Length..].Split('.'))
        {
            var bracket = rawSegment.IndexOf('[');
            var propertyName = bracket < 0 ? rawSegment : rawSegment[..bracket];
            if (value is not JsonObject current ||
                !TryGetProperty(current, propertyName, out value))
            {
                value = null;
                return false;
            }

            var position = bracket;
            while (position >= 0)
            {
                var close = rawSegment.IndexOf(']', position + 1);
                if (close < 0 ||
                    !int.TryParse(
                        rawSegment[(position + 1)..close],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var index) ||
                    value is not JsonArray array ||
                    index < 0 ||
                    index >= array.Count)
                {
                    value = null;
                    return false;
                }

                value = array[index];
                position = rawSegment.IndexOf('[', close + 1);
            }
        }

        return true;
    }

    private static bool TryGetProperty(
        JsonObject source,
        string name,
        out JsonNode? value)
    {
        foreach (var property in source)
        {
            if (property.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
