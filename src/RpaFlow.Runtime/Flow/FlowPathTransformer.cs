using System.Text.Json.Nodes;
using RpaFlow.Contracts;

namespace RpaFlow.Runtime;

public static class FlowPathTransformer
{
    public static void Execute(
        FlowActionDefinition action,
        FlowDataContext data)
    {
        var path = FlowValueResolver.ResolveRequired(action, data);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' recebeu um caminho vazio.");
        }

        var result = action.Operation?.ToLowerInvariant() switch
        {
            "filename" => GetFileName(path, action.Name),
            "filenamewithoutextension" => GetFileNameWithoutExtension(path, action.Name),
            "extension" => GetExtension(path, action.Name),
            "directoryname" => GetDirectoryName(path),
            _ => throw new InvalidOperationException(
                $"Operação de caminho não interpretada em '{action.Name}': '{action.Operation}'.")
        };

        data.SetRuntimeValue(action.Target!, JsonValue.Create(result));
    }

    private static string GetFileName(string path, string actionName)
    {
        if (IsSeparator(path[^1]))
        {
            throw new InvalidOperationException(
                $"A ação '{actionName}' exige um caminho de arquivo, mas recebeu uma pasta.");
        }

        var separator = LastSeparatorIndex(path);
        var fileName = separator < 0 ? path : path[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException(
                $"A ação '{actionName}' não conseguiu obter o nome do arquivo.");
        }

        return fileName;
    }

    private static string GetFileNameWithoutExtension(
        string path,
        string actionName)
    {
        var fileName = GetFileName(path, actionName);
        var extensionIndex = LastExtensionIndex(fileName);
        return extensionIndex < 0 ? fileName : fileName[..extensionIndex];
    }

    private static string GetExtension(string path, string actionName)
    {
        var fileName = GetFileName(path, actionName);
        var extensionIndex = LastExtensionIndex(fileName);
        return extensionIndex < 0 ? string.Empty : fileName[extensionIndex..];
    }

    private static string GetDirectoryName(string path)
    {
        var separator = LastSeparatorIndex(path);
        if (separator < 0)
        {
            return string.Empty;
        }

        if (separator == 0 ||
            (separator == 2 && path.Length >= 3 && path[1] == ':'))
        {
            return path[..(separator + 1)];
        }

        return path[..separator];
    }

    private static int LastSeparatorIndex(string path) =>
        Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));

    private static int LastExtensionIndex(string fileName)
    {
        var index = fileName.LastIndexOf('.');
        return index <= 0 ? -1 : index;
    }

    private static bool IsSeparator(char value) => value is '/' or '\\';
}
