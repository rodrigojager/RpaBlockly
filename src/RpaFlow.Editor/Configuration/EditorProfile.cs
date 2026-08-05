using System.Text.RegularExpressions;
namespace RpaFlow.Editor.Configuration;

public sealed partial class EditorProfile
{
    private static readonly HashSet<string> SupportedFieldTypes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "text", "url", "email", "password", "date", "number", "checkbox",
        "stringList"
    };

    public string DisplayName { get; set; } = string.Empty;

    public string ProjectFile { get; set; } = string.Empty;

    public string ConfigurationFile { get; set; } = "appsettings.local.json";

    public string FlowFile { get; set; } = "flow.production.json";

    public List<EditorConfigurationField> ConfigurationFields { get; set; } = [];

    public void Validate()
    {
        Require(DisplayName, "displayName");
        Require(ProjectFile, "projectFile");
        Require(ConfigurationFile, "configurationFile");
        Require(FlowFile, "flowFile");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < ConfigurationFields.Count; index++)
        {
            var field = ConfigurationFields[index];
            var prefix = $"configurationFields[{index}]";
            if (!ConfigurationPath().IsMatch(field.Path) || !paths.Add(field.Path))
            {
                throw new InvalidOperationException(
                    $"{prefix}.path é inválido ou está duplicado: '{field.Path}'.");
            }

            Require(field.Label, $"{prefix}.label");
            if (!SupportedFieldTypes.Contains(field.Type))
            {
                throw new InvalidOperationException(
                    $"{prefix}.type não é suportado: '{field.Type}'.");
            }
        }
    }

    private static void Require(string? value, string property)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{property} é obrigatório no perfil do editor.");
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*(\\.[A-Za-z][A-Za-z0-9_-]*)*$")]
    private static partial Regex ConfigurationPath();
}

public sealed class EditorConfigurationField
{
    public string Path { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string? Source { get; set; }

    public string Type { get; set; } = "text";

    public bool Nullable { get; set; }
}
