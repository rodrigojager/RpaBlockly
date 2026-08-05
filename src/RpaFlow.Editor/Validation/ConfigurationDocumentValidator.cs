using System.Text.Json;
using System.Text.RegularExpressions;
using RpaFlow.Editor.Configuration;

namespace RpaFlow.Editor.Validation;

public static partial class ConfigurationDocumentValidator
{
    public static void Validate(JsonElement root, EditorProfile profile)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("A configuração deve ser um objeto JSON.");
        }

        foreach (var field in profile.ConfigurationFields)
        {
            if (TryResolvePath(root, field.Path, out var value))
            {
                ValidateField(value, field);
            }
        }

        ValidateBlocklyVariables(root);
    }

    private static bool TryResolvePath(
        JsonElement root,
        string path,
        out JsonElement value)
    {
        var current = root;
        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(current, part, out current))
            {
                value = default;
                return false;
            }
        }

        value = current;
        return true;
    }

    private static void ValidateField(
        JsonElement value,
        EditorConfigurationField field)
    {
        if (value.ValueKind == JsonValueKind.Null && field.Nullable)
        {
            return;
        }

        var valid = field.Type.ToLowerInvariant() switch
        {
            "checkbox" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "number" => value.ValueKind == JsonValueKind.Number,
            "stringlist" => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
            _ => value.ValueKind == JsonValueKind.String
        };
        if (!valid)
        {
            throw new InvalidOperationException(
                $"A propriedade {field.Path} não possui um valor compatível com {field.Type}.");
        }
    }

    private static void ValidateBlocklyVariables(JsonElement root)
    {
        if (!TryGetProperty(root, "Blockly", out var blockly))
        {
            return;
        }

        if (blockly.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Blockly deve ser um objeto JSON.");
        }

        if (!TryGetProperty(blockly, "Variables", out var variables))
        {
            return;
        }

        if (variables.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Blockly.Variables deve ser um objeto JSON.");
        }

        foreach (var variable in variables.EnumerateObject())
        {
            if (!VariableName().IsMatch(variable.Name))
            {
                throw new InvalidOperationException(
                    $"Nome de variável inválido: '{variable.Name}'. " +
                    "Use letras, números, ponto, hífen ou sublinhado.");
            }

            if (variable.Value.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidOperationException(
                    $"A variável '{variable.Name}' possui um valor JSON indefinido.");
            }
        }
    }

    private static bool TryGetProperty(
        JsonElement parent,
        string property,
        out JsonElement value)
    {
        foreach (var candidate in parent.EnumerateObject())
        {
            if (candidate.Name.Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]*$")]
    private static partial Regex VariableName();
}
