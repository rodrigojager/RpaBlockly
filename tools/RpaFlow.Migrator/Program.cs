using System.Text;
using System.Text.Json;
using RpaFlow.Contracts;
using RpaFlow.Contracts.V2;
using RpaFlow.Migrator;
using RpaFlow.Packages;

var command = MigratorCommand.Parse(args);
var inputs = ResolveInputs(command.Input, command.Batch);
if (inputs.Count == 0)
{
    throw new FileNotFoundException("Nenhum flow.production.json V1 foi encontrado.");
}

var results = new List<(string Source, string RpaId, MigrationResult Result)>();
foreach (var input in inputs)
{
    var flow = await new JsonFlowLoader().LoadAsync(input, CancellationToken.None);
    var rpaId = command.RpaId ?? ToRpaId(Path.GetFileName(Path.GetDirectoryName(input)!));
    if (inputs.Count > 1 && command.RpaId is not null)
    {
        rpaId = $"{command.RpaId}-{results.Count + 1}";
    }
    var result = new V1ToV2Migrator().Migrate(flow, input);
    results.Add((input, rpaId, result));
    Console.WriteLine(
        $"{Path.GetFileName(input)}: {result.Report.ActionCount} ações, " +
        $"{result.Report.LocatorCount} locators, pacote válido.");
}

if (command.DryRun)
{
    Console.WriteLine("Dry-run concluído; nenhum arquivo foi alterado.");
    return;
}

PrepareOutput(command.Output!, command.Force);
foreach (var item in results)
{
    var destination = inputs.Count == 1
        ? Path.Combine(command.Output!, "package")
        : Path.Combine(command.Output!, item.RpaId, "package");
    Directory.CreateDirectory(destination);
    await WriteDocumentAsync(
        Path.Combine(destination, "flow.production.json"),
        item.Result.Documents.Flow);
    await WriteDocumentAsync(
        Path.Combine(destination, "locators.production.json"),
        item.Result.Documents.Locators);
    await WriteDocumentAsync(
        Path.Combine(destination, "rpa.policy.json"),
        item.Result.Documents.Policy);
    await WriteDocumentAsync(
        Path.Combine(destination, "migration-report.json"),
        item.Result.Report);

    if (command.PublishStore is not null)
    {
        var store = new FileRpaPackageStore(command.PublishStore);
        var published = await store.PublishAsync(
            item.RpaId,
            item.Result.Documents,
            expectedRevision: null,
            CancellationToken.None);
        Console.WriteLine(
            $"Package store: {item.RpaId} em {published.Revision.Value}.");
    }
}

Console.WriteLine($"Migração concluída em: {command.Output}");

static IReadOnlyList<string> ResolveInputs(string input, bool batch)
{
    var fullPath = Path.GetFullPath(input);
    if (File.Exists(fullPath)) return [fullPath];
    if (!Directory.Exists(fullPath))
    {
        throw new DirectoryNotFoundException($"Entrada não encontrada: {fullPath}");
    }
    var direct = Path.Combine(fullPath, "flow.production.json");
    if (!batch && File.Exists(direct)) return [direct];
    if (!batch)
    {
        throw new FileNotFoundException(
            "O diretório não contém flow.production.json. Use --batch para busca recursiva.",
            direct);
    }
    return Directory.EnumerateFiles(
            fullPath,
            "flow.production.json",
            SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static void PrepareOutput(string output, bool force)
{
    var fullPath = Path.GetFullPath(output);
    if (!Directory.Exists(fullPath))
    {
        Directory.CreateDirectory(fullPath);
        return;
    }
    if (!Directory.EnumerateFileSystemEntries(fullPath).Any()) return;
    if (!force)
    {
        throw new InvalidOperationException(
            $"A saída já contém arquivos: {fullPath}. Use --force para criar backup.");
    }
    var backup = fullPath + ".backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
    Directory.Move(fullPath, backup);
    Directory.CreateDirectory(fullPath);
    Console.WriteLine($"Backup preservado em: {backup}");
}

static Task WriteDocumentAsync<T>(string path, T value)
{
    var json = JsonSerializer.Serialize(value, V2JsonSerializer.WriteOptions)
        .ReplaceLineEndings("\n") + "\n";
    return File.WriteAllTextAsync(path, json, new UTF8Encoding(false, true));
}

static string ToRpaId(string value)
{
    var normalized = new string(value.ToLowerInvariant().Select(character =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
            ? character
            : '-').ToArray()).Trim('-');
    return string.IsNullOrWhiteSpace(normalized) ? "rpa-migrado" : normalized;
}

internal sealed record MigratorCommand(
    string Input,
    string? Output,
    string? PublishStore,
    string? RpaId,
    bool Batch,
    bool DryRun,
    bool Force)
{
    public static MigratorCommand Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Uso: RpaFlow.Migrator <arquivo|diretório> --output <pasta> " +
                "[--dry-run] [--batch] [--rpa-id <id>] [--publish-store <pasta>] [--force]");
        }
        var dryRun = Has(args, "--dry-run");
        var output = Value(args, "--output");
        if (!dryRun && string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("--output é obrigatório fora do modo --dry-run.");
        }
        return new MigratorCommand(
            args[0],
            output is null ? null : Path.GetFullPath(output),
            Value(args, "--publish-store") is { } store ? Path.GetFullPath(store) : null,
            Value(args, "--rpa-id"),
            Has(args, "--batch"),
            dryRun,
            Has(args, "--force"));
    }

    private static bool Has(string[] args, string name) =>
        args.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string? Value(string[] args, string name)
    {
        var index = Array.FindIndex(args, item =>
            item.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Informe um valor depois de {name}.");
        }
        return args[index + 1];
    }
}
