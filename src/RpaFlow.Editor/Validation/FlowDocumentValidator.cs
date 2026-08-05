using System.Text.Json;
using System.Text.RegularExpressions;
using RpaFlow.Contracts;

namespace RpaFlow.Editor.Validation;

public static class FlowDocumentValidator
{
    private static readonly Regex RuntimeTargetPattern = new(
        "^runtime\\.[A-Za-z][A-Za-z0-9_-]*(\\.[A-Za-z][A-Za-z0-9_-]*)*$",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));
    private static readonly Regex DataPathPattern = new(
        "^(input|job|config|variables|attachments|runtime|system|loop)" +
        "\\.[A-Za-z][A-Za-z0-9_-]*(\\[[0-9]+\\])?" +
        "(\\.[A-Za-z][A-Za-z0-9_-]*(\\[[0-9]+\\])?)*$",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));
    private static readonly Regex ProviderAliasPattern = new(
        "^[A-Za-z][A-Za-z0-9._-]*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    public static void Validate(JsonElement root)
    {
        try
        {
            var definition = FlowJsonSerializer.Deserialize(root);
            FlowDefinitionValidator.Validate(definition);
            ValidateEditorActions(root);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"O fluxo JSON não corresponde ao schema 1: {exception.Message}",
                exception);
        }
    }

    private static void ValidateEditorActions(JsonElement root)
    {
        var errors = new List<string>();
        if (TryGetProperty(root, "actions", out var actions))
        {
            ValidateActionList(actions, "actions", errors);
        }

        if (TryGetProperty(root, "subflows", out var subflows) &&
            subflows.ValueKind == JsonValueKind.Object)
        {
            foreach (var subflow in subflows.EnumerateObject())
            {
                ValidateActionList(
                    subflow.Value,
                    $"subflows.{subflow.Name}",
                    errors);
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Fluxo de produção inválido:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
        }
    }

    private static void ValidateActionList(
        JsonElement actions,
        string path,
        ICollection<string> errors)
    {
        if (actions.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var action in actions.EnumerateArray())
        {
            var prefix = $"{path}[{index}]";
            index++;
            if (action.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = GetString(action, "type");
            if (type?.Equals("captureTimestamp", StringComparison.OrdinalIgnoreCase) == true)
            {
                ValidateRuntimeTarget(action, prefix, errors);
            }
            else if (type?.Equals(
                         "waitForOneTimeCode",
                         StringComparison.OrdinalIgnoreCase) == true)
            {
                ValidateWaitForOneTimeCode(action, prefix, errors);
            }

            if (TryGetProperty(action, "actions", out var nestedActions))
            {
                ValidateActionList(nestedActions, $"{prefix}.actions", errors);
            }

            if (TryGetProperty(action, "elseActions", out var elseActions))
            {
                ValidateActionList(elseActions, $"{prefix}.elseActions", errors);
            }
        }
    }

    private static void ValidateWaitForOneTimeCode(
        JsonElement action,
        string prefix,
        ICollection<string> errors)
    {
        ValidateRuntimeTarget(action, prefix, errors);

        var providerAlias = GetString(action, "providerAlias");
        if (providerAlias is null || !ProviderAliasPattern.IsMatch(providerAlias))
        {
            errors.Add(
                $"{prefix}.providerAlias deve ser um alias não vazio, começando por letra e usando somente letras, números, ponto, hífen ou sublinhado.");
        }

        var notBeforeSource = GetString(action, "notBeforeSource");
        if (notBeforeSource is null || !DataPathPattern.IsMatch(notBeforeSource))
        {
            errors.Add(
                $"{prefix}.notBeforeSource deve ser um caminho de dados válido.");
        }

        var timeoutMs = GetInteger(action, "timeoutMs");
        if (timeoutMs is null or < 1000 or > 600_000)
        {
            errors.Add($"{prefix}.timeoutMs deve estar entre 1000 e 600000.");
        }

        var pollIntervalMs = GetInteger(action, "pollIntervalMs");
        if (pollIntervalMs is null or < 500 or > 60_000)
        {
            errors.Add($"{prefix}.pollIntervalMs deve estar entre 500 e 60000.");
        }

        if (timeoutMs is not null &&
            pollIntervalMs is not null &&
            pollIntervalMs > timeoutMs)
        {
            errors.Add(
                $"{prefix}.pollIntervalMs não pode exceder timeoutMs.");
        }
    }

    private static void ValidateRuntimeTarget(
        JsonElement action,
        string prefix,
        ICollection<string> errors)
    {
        var target = GetString(action, "target");
        if (target is null || !RuntimeTargetPattern.IsMatch(target))
        {
            errors.Add(
                $"{prefix}.target deve usar runtime.<caminho> sem índice de lista.");
        }
    }

    private static int? GetInteger(JsonElement value, string propertyName) =>
        TryGetProperty(value, propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var result)
            ? result
            : null;

    private static string? GetString(JsonElement value, string propertyName) =>
        TryGetProperty(value, propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!.Trim()
            : null;

    private static bool TryGetProperty(
        JsonElement value,
        string propertyName,
        out JsonElement property)
    {
        foreach (var candidate in value.EnumerateObject())
        {
            if (candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}
