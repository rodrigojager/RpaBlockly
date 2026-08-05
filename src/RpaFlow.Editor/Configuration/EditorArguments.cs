namespace RpaFlow.Editor.Configuration;

public sealed record EditorArguments(
    string? ProjectRoot,
    string? ConfigurationFile,
    string? FlowFile,
    bool OpenBrowser,
    string Url)
{
    public static EditorArguments Parse(string[] args)
    {
        return new EditorArguments(
            ReadValue(args, "--project-root"),
            ReadValue(args, "--configuration"),
            ReadValue(args, "--flow"),
            !args.Contains("--no-open", StringComparer.OrdinalIgnoreCase),
            ReadValue(args, "--url") ?? "http://127.0.0.1:5187");
    }

    private static string? ReadValue(string[] args, string name)
    {
        var index = Array.FindIndex(
            args,
            argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Informe um valor depois de {name}.");
        }

        return args[index + 1];
    }
}
