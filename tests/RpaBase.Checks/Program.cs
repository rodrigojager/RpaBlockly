using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RpaFlow.Contracts;
using RpaFlow.Packages;
using RpaFlow.Playwright.V2;

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var expectedActionTypes = FlowActionCatalog.SupportedTypes;
Check(expectedActionTypes.Count == 33, "o catálogo oficial contém 33 tipos de ação");

var editorCatalogPath = Path.Combine(
    repositoryRoot,
    "src",
    "RpaFlow.Editor",
    "wwwroot",
    "v2",
    "action-catalog.js");
var editorCatalog = ReadStrict(editorCatalogPath);
var calls = Regex.Matches(
    editorCatalog,
    "(?:entry|variant|control)\\(\\s*\"(?<action>[^\"]+)\"\\s*,\\s*\"(?<block>[^\"]+)\"",
    RegexOptions.CultureInvariant);
var editorActionTypes = calls
    .Select(match => match.Groups["action"].Value)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var editorBlockTypes = calls
    .Select(match => match.Groups["block"].Value)
    .Append("rpa_subflow_definition")
    .ToHashSet(StringComparer.Ordinal);
Check(editorActionTypes.SetEquals(expectedActionTypes),
    "o editor cobre exatamente os 33 tipos do runtime");
Check(editorBlockTypes.Count == 36, "o editor preserva os 36 blocos do catálogo V2");

var flowSchema = ReadStrict(Path.Combine(repositoryRoot, "schemas", "flow-v2.schema.json"));
Check(!Regex.IsMatch(
        flowSchema,
        "\"(?:selector|scope|frameSelectors|triggerSelector|optionSelector|readySelector|successSelector|protocolSelector)\"",
        RegexOptions.CultureInvariant),
    "o contrato de fluxo V2 não possui campos de seletor embutido");

await CheckOperationalPackageAsync(
    repositoryRoot,
    Path.Combine("examples", "RpaExemplo", "package-store"),
    "rpa-exemplo");
await CheckOperationalPackageAsync(
    repositoryRoot,
    Path.Combine("templates", "rpa-web", "package-store"),
    "rpa-template");

var contractsAssembly = typeof(FlowActionCatalog).Assembly;
var playwrightAssembly = typeof(PlaywrightV2FlowExecutor).Assembly;
Check(contractsAssembly.GetType("RpaFlow.Contracts.FlowDefinition") is null,
    "o assembly de contratos operacional não contém DTO V1");
Check(playwrightAssembly.GetType("RpaFlow.Playwright.PlaywrightFlowExecutor") is null,
    "o assembly Playwright operacional não contém interpretador V1");
Check(
    ProductionAssemblies().All(assembly => assembly.GetReferencedAssemblies().All(
        reference => !reference.Name!.StartsWith("RpaFlow.Legacy", StringComparison.Ordinal))),
    "assemblies de produção não referenciam assemblies históricos");

var ownedFiles = EnumerateOwnedTextFiles(repositoryRoot).ToArray();
foreach (var file in ownedFiles)
{
    _ = ReadStrict(file);
}
Check(true, "arquivos textuais próprios são UTF-8 estrito");

var forbiddenClientTerms = new[]
{
    string.Concat("le", "af"),
    string.Concat("neo", "energia"),
    string.Concat("mar", "cus")
};
foreach (var file in ownedFiles)
{
    var content = ReadStrict(file);
    var term = forbiddenClientTerms.FirstOrDefault(value =>
        content.Contains(value, StringComparison.OrdinalIgnoreCase));
    if (term is not null)
    {
        throw new InvalidOperationException(
            $"Referência externa proibida '{term}' em {Path.GetRelativePath(repositoryRoot, file)}.");
    }

    if (HasMojibake(content))
    {
        throw new InvalidOperationException(
            $"Possível mojibake em {Path.GetRelativePath(repositoryRoot, file)}.");
    }

    var privateKeyHeader = string.Concat("-----BEGIN ", "PRIVATE KEY-----");
    var rsaPrivateKeyHeader = string.Concat("-----BEGIN RSA ", "PRIVATE KEY-----");
    if (content.Contains(privateKeyHeader, StringComparison.Ordinal) ||
        content.Contains(rsaPrivateKeyHeader, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Chave privada encontrada em {Path.GetRelativePath(repositoryRoot, file)}.");
    }
}
Check(true, "não há referência de cliente externo, mojibake nem chave privada");

var releaseMetadata = JsonNode.Parse(ReadStrict(
    Path.Combine(repositoryRoot, "release", "2.0.0-rc.1.json")))?.AsObject()
    ?? throw new InvalidOperationException("Metadados do release candidate estão vazios.");
var acceptanceReport = ReadStrict(Path.Combine(
    repositoryRoot,
    "docs",
    "recorder",
    "relatorio-instalacao-limpa.md"));
Check(
    releaseMetadata["releaseStatus"]?.GetValue<string>() == "release-candidate" &&
    releaseMetadata["humanAcceptance"]?["requirement"]?.GetValue<string>() == "REC-140" &&
    releaseMetadata["humanAcceptance"]?["status"]?.GetValue<string>() == "pending" &&
    acceptanceReport.Contains(
        "PENDENTE — NÃO EXECUTADO POR PESSOA INDEPENDENTE",
        StringComparison.Ordinal),
    "release permanece RC enquanto o aceite humano REC-140 está pendente");

var forbiddenNames = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
    .Where(path => !IsIgnored(repositoryRoot, path))
    .Select(Path.GetFileName)
    .Where(name => name is not null &&
        (name.Equals("appsettings.local.json", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("secrets.json", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)))
    .ToArray();
Check(forbiddenNames.Length == 0,
    "o repositório não contém configuração local, cofre exportado ou certificado privado");

Console.WriteLine("Baseline e fronteira operacional V2 validados com sucesso.");

static IEnumerable<Assembly> ProductionAssemblies()
{
    yield return typeof(FlowActionCatalog).Assembly;
    yield return typeof(RpaPackageSnapshot).Assembly;
    yield return typeof(PlaywrightV2FlowExecutor).Assembly;
}

static async Task CheckOperationalPackageAsync(
    string root,
    string relativeStore,
    string rpaId)
{
    var store = new FileRpaPackageStore(Path.Combine(root, relativeStore));
    var snapshot = await store.LoadAsync(rpaId, null, CancellationToken.None);
    Check(snapshot.Flow.SchemaVersion == 2, $"{rpaId} usa fluxo schema 2");
    Check(snapshot.Policy.LocatorResilience.Mode ==
          RpaFlow.Contracts.V2.LocatorResilienceMode.Strict,
        $"{rpaId} parte de política strict");
}

static IEnumerable<string> EnumerateOwnedTextFiles(string root)
{
    var extensions = new HashSet<string>(
        [".cs", ".csproj", ".slnx", ".json", ".js", ".ts", ".md", ".html", ".css", ".sql", ".yml", ".yaml", ".ps1"],
        StringComparer.OrdinalIgnoreCase);
    return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !IsIgnored(root, path))
        .Where(path => extensions.Contains(Path.GetExtension(path)));
}

static bool IsIgnored(string root, string path)
{
    var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
    return relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
        relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
        relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
        relative.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
        relative.StartsWith(
            "src/RpaFlow.Recorder.Extension/build/",
            StringComparison.OrdinalIgnoreCase) ||
        relative.StartsWith(
            "src/RpaFlow.Recorder.Extension/.test-build/",
            StringComparison.OrdinalIgnoreCase) ||
        relative.Contains("/__pycache__/", StringComparison.OrdinalIgnoreCase) ||
        relative.StartsWith("tmp/", StringComparison.OrdinalIgnoreCase) ||
        relative.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) ||
        relative.Contains("/wwwroot/vendor/", StringComparison.OrdinalIgnoreCase);
}

static bool HasMojibake(string content)
{
    if (content.Contains('\uFFFD'))
    {
        return true;
    }

    for (var index = 0; index + 1 < content.Length; index++)
    {
        if (content[index] == '\u00C3' && content[index + 1] is >= '\u0080' and <= '\u00BF')
        {
            return true;
        }

        if (content[index] == '\u00C2' && content[index + 1] == '\u00A0')
        {
            return true;
        }

        if (index + 2 < content.Length &&
            content[index] == '\u00E2' &&
            content[index + 1] == '\u20AC')
        {
            return true;
        }
    }

    return false;
}

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

    throw new DirectoryNotFoundException("A raiz do repositório não foi encontrada.");
}

static void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Falha: {description}.");
    }

    Console.WriteLine($"OK: {description}.");
}
