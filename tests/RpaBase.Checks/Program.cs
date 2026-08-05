using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RpaFlow.Contracts;

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var strictUtf8 = new UTF8Encoding(false, true);

var flowPath = Path.Combine(
    repositoryRoot,
    "examples",
    "RpaExemplo",
    "flow.production.json");
var flow = await new JsonFlowLoader().LoadAsync(flowPath, CancellationToken.None);
Check(flow.SchemaVersion == 1, "o fluxo de exemplo usa schema 1");
Check(flow.Actions.Count > 0, "o fluxo de exemplo possui ação");

var appJs = ReadStrict(Path.Combine(
    repositoryRoot,
    "src",
    "RpaFlow.Editor",
    "wwwroot",
    "app.js"));
var implementedBlocks = Regex.Matches(appJs, @"rpa_[a-z0-9_]+")
    .Select(match => match.Value)
    .ToHashSet(StringComparer.Ordinal);
Check(implementedBlocks.Count == 35, "o editor expõe 35 blocos distintos");

var catalog = ReadStrict(Path.Combine(
    repositoryRoot,
    "docs",
    "assets",
    "block-catalog.js"));
var documentedBlocks = Regex.Matches(
        catalog,
        "blockType:\\s*\"(?<type>rpa_[a-z0-9_]+)\"")
    .Select(match => match.Groups["type"].Value)
    .ToHashSet(StringComparer.Ordinal);
var missingDocumentation = implementedBlocks.Except(documentedBlocks).ToArray();
var unknownDocumentation = documentedBlocks.Except(implementedBlocks).ToArray();
Check(
    missingDocumentation.Length == 0,
    "todos os blocos do editor possuem seção no manual",
    missingDocumentation);
Check(
    unknownDocumentation.Length == 0 && documentedBlocks.Count == 35,
    "o manual não documenta blocos inexistentes",
    unknownDocumentation);

var documentedActionTypes = Regex.Matches(
        catalog,
        "actionType:\\s*\"(?<type>[A-Za-z][A-Za-z0-9.]*)\"")
    .Select(match => match.Groups["type"].Value)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var missingActionTypes = FlowActionCatalog.SupportedTypes
    .Except(documentedActionTypes, StringComparer.OrdinalIgnoreCase)
    .ToArray();
Check(
    missingActionTypes.Length == 0,
    "todos os tipos do runtime estão documentados",
    missingActionTypes);

var workerConfiguration = JsonNode.Parse(ReadStrict(Path.Combine(
    repositoryRoot,
    "src",
    "Rpa.Worker",
    "appsettings.example.json")))!.AsObject();
Check(
    string.IsNullOrWhiteSpace(
        workerConfiguration["ConnectionStrings"]?["RpaDatabase"]?.GetValue<string>()),
    "a string de conexão do exemplo está vazia");
Check(
    workerConfiguration["RpaWorker"]?["Enabled"]?.GetValue<bool>() == false,
    "o worker de exemplo nasce desabilitado");
Check(
    workerConfiguration["RpaWorker"]?["ExecutionMode"]?.GetValue<string>() ==
        "SafeValidation",
    "o worker de exemplo nasce em validação segura");
var emailReader = workerConfiguration["RpaWorker"]?["EmailReader"]?.AsObject()
    ?? throw new InvalidOperationException("EmailReader não foi encontrado no exemplo.");
Check(
    string.IsNullOrWhiteSpace(emailReader["TenantId"]?.GetValue<string>()) &&
    string.IsNullOrWhiteSpace(emailReader["ClientId"]?.GetValue<string>()) &&
    string.IsNullOrWhiteSpace(emailReader["ClientSecret"]?.GetValue<string>()),
    "o exemplo não contém credenciais do Microsoft Graph");
Check(
    emailReader["Providers"]?["email-otp"]?["Enabled"]?.GetValue<bool>() == false,
    "o provider de OTP do exemplo nasce desabilitado");

var ignored = ReadStrict(Path.Combine(repositoryRoot, ".gitignore"));
Check(ignored.Contains("appsettings.local.json", StringComparison.Ordinal),
    "configuração local está ignorada");
Check(ignored.Contains("storage/", StringComparison.Ordinal),
    "sessões e artefatos do worker estão ignorados");

var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".cs", ".csproj", ".json", ".md", ".html", ".css", ".js", ".ps1",
    ".cmd", ".sql", ".slnx", ".txt"
};
var invalidUtf8 = new List<string>();
foreach (var file in Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories))
{
    if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        !textExtensions.Contains(Path.GetExtension(file)))
    {
        continue;
    }

    try
    {
        _ = strictUtf8.GetString(await File.ReadAllBytesAsync(file));
    }
    catch (DecoderFallbackException)
    {
        invalidUtf8.Add(Path.GetRelativePath(repositoryRoot, file));
    }
}

Check(invalidUtf8.Count == 0, "todos os textos estão em UTF-8 estrito", invalidUtf8);
Console.WriteLine("Base genérica validada com sucesso.");

static string ReadStrict(string path) =>
    new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(Path.GetFullPath(start));
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "RpaBlockly.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("A raiz da base RPA não foi encontrada.");
}

static void Check(
    bool condition,
    string description,
    IReadOnlyCollection<string>? details = null)
{
    if (!condition)
    {
        var suffix = details is { Count: > 0 }
            ? $": {string.Join(", ", details)}"
            : string.Empty;
        throw new InvalidOperationException($"Falha: {description}{suffix}");
    }

    Console.WriteLine($"OK: {description}.");
}
