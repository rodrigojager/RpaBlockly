using System.Globalization;
using System.Text.Json.Nodes;
namespace RpaFlow.Runtime;

public sealed class FlowDataContext
{
    private readonly JsonObject _input;
    private readonly JsonObject _configuration;
    private readonly JsonObject _attachments;
    private readonly JsonObject _runtime = [];
    private readonly JsonObject _system;
    private readonly List<IReadOnlyDictionary<string, JsonNode?>> _loopScopes = [];

    public FlowDataContext(FlowExecutionRequest request)
    {
        _input = (JsonObject)request.Input.DeepClone();
        _configuration = (JsonObject)request.Configuration.DeepClone();
        _attachments = (JsonObject)request.Attachments.DeepClone();
        _system = new JsonObject
        {
            ["executionId"] = request.ExecutionId,
            ["workItemId"] = request.WorkItemId,
            ["batchId"] = request.BatchId
        };
    }

    public JsonObject ExportRuntime() => (JsonObject)_runtime.DeepClone();

    public bool TryResolve(string path, out JsonNode? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var segments = ParsePath(path);
        if (segments.Count == 0)
        {
            return false;
        }

        var root = segments[0].Name.ToLowerInvariant();
        if (root == "loop")
        {
            return TryResolveLoop(segments, out value);
        }

        JsonNode? current = root switch
        {
            "input" => _input,
            "job" => _input,
            "config" => _configuration,
            "variables" => _configuration,
            "attachments" => _attachments,
            "runtime" => _runtime,
            "system" => _system,
            _ => null
        };
        if (current is null)
        {
            return false;
        }

        if ((root is "config" or "variables") && segments.Count > 1)
        {
            var legacyKey = string.Join('.', segments.Skip(1).Select(segment => segment.Raw));
            if (TryGetProperty(_configuration, legacyKey, out var legacyValue))
            {
                value = legacyValue;
                return true;
            }
        }

        return TryTraverse(current, segments, 1, out value);
    }

    public JsonNode? ResolveRequired(string path, string description)
    {
        if (!TryResolve(path, out var value))
        {
            throw new InvalidOperationException(
                $"{description} não encontrou o caminho de dados '{path}'.");
        }

        return value;
    }

    public void SetRuntimeValue(string target, JsonNode? value)
    {
        var segments = ParsePath(target);
        if (segments.Count < 2 ||
            !segments[0].Name.Equals("runtime", StringComparison.OrdinalIgnoreCase) ||
            segments.Any(segment => segment.Indexes.Count > 0))
        {
            throw new InvalidOperationException(
                $"O destino '{target}' deve usar runtime.<caminho> sem índices de lista.");
        }

        var current = _runtime;
        for (var index = 1; index < segments.Count - 1; index++)
        {
            var name = segments[index].Name;
            if (!TryGetProperty(current, name, out var child) || child is null)
            {
                var created = new JsonObject();
                SetProperty(current, name, created);
                current = created;
                continue;
            }

            if (child is not JsonObject childObject)
            {
                throw new InvalidOperationException(
                    $"Não é possível criar '{target}': '{name}' já contém um valor simples ou lista.");
            }

            current = childObject;
        }

        SetProperty(current, segments[^1].Name, value?.DeepClone());
    }

    public IDisposable PushLoopScope(
        string itemVariable,
        JsonNode? item,
        string indexVariable,
        int index)
    {
        var scope = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            [itemVariable] = item,
            [indexVariable] = JsonValue.Create(index)
        };
        _loopScopes.Add(scope);
        return new FlowScope(() => _loopScopes.RemoveAt(_loopScopes.Count - 1));
    }

    public IDisposable PushLoopIndex(string variable, int index)
    {
        var scope = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            [variable] = JsonValue.Create(index)
        };
        _loopScopes.Add(scope);
        return new FlowScope(() => _loopScopes.RemoveAt(_loopScopes.Count - 1));
    }

    private bool TryResolveLoop(IReadOnlyList<PathSegment> segments, out JsonNode? value)
    {
        value = null;
        if (segments.Count < 2)
        {
            return false;
        }

        for (var index = _loopScopes.Count - 1; index >= 0; index--)
        {
            if (!_loopScopes[index].TryGetValue(segments[1].Name, out var scopedValue))
            {
                continue;
            }

            if (!TryApplyIndexes(scopedValue, segments[1].Indexes, out scopedValue))
            {
                return false;
            }

            return TryTraverse(scopedValue, segments, 2, out value);
        }

        return false;
    }

    private static bool TryTraverse(
        JsonNode? current,
        IReadOnlyList<PathSegment> segments,
        int startIndex,
        out JsonNode? value)
    {
        for (var index = startIndex; index < segments.Count; index++)
        {
            if (current is not JsonObject currentObject ||
                !TryGetProperty(currentObject, segments[index].Name, out current))
            {
                value = null;
                return false;
            }

            if (!TryApplyIndexes(current, segments[index].Indexes, out current))
            {
                value = null;
                return false;
            }
        }

        value = current;
        return true;
    }

    private static bool TryApplyIndexes(
        JsonNode? current,
        IReadOnlyList<int> indexes,
        out JsonNode? value)
    {
        foreach (var index in indexes)
        {
            if (current is not JsonArray array || index < 0 || index >= array.Count)
            {
                value = null;
                return false;
            }

            current = array[index];
        }

        value = current;
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

    private static void SetProperty(JsonObject target, string name, JsonNode? value)
    {
        var existingName = target
            .Select(property => property.Key)
            .FirstOrDefault(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
        target[existingName ?? name] = value;
    }

    private static List<PathSegment> ParsePath(string path)
    {
        var result = new List<PathSegment>();
        foreach (var rawSegment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bracketIndex = rawSegment.IndexOf('[');
            var name = bracketIndex < 0 ? rawSegment : rawSegment[..bracketIndex];
            if (string.IsNullOrWhiteSpace(name))
            {
                return [];
            }

            var indexes = new List<int>();
            var position = bracketIndex;
            while (position >= 0 && position < rawSegment.Length)
            {
                var close = rawSegment.IndexOf(']', position + 1);
                if (close < 0 ||
                    !int.TryParse(
                        rawSegment[(position + 1)..close],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsedIndex))
                {
                    return [];
                }

                indexes.Add(parsedIndex);
                position = rawSegment.IndexOf('[', close + 1);
                if (position < 0 && close != rawSegment.Length - 1)
                {
                    return [];
                }
            }

            result.Add(new PathSegment(rawSegment, name, indexes));
        }

        return result;
    }

    private sealed record PathSegment(
        string Raw,
        string Name,
        IReadOnlyList<int> Indexes);

    private sealed class FlowScope(Action restore) : IDisposable
    {
        private Action? _restore = restore;

        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
