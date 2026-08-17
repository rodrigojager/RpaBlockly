using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RpaFlow.Contracts;
using RpaFlow.Runtime;

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

var configuredConfirmation = CreateSafeFinalConfirmation(includeFeedback: true);
FlowDefinitionValidator.Validate(CreateSingleActionFlow(configuredConfirmation));
Check(true, "a confirmação final aceita o conjunto completo de feedback");

var legacyConfirmation = CreateSafeFinalConfirmation(includeFeedback: false);
FlowDefinitionValidator.Validate(CreateSingleActionFlow(legacyConfirmation));
Check(true, "a confirmação final segura legada permanece compatível");

ExpectInvalid(
    () =>
    {
        var action = CreateSafeFinalConfirmation(includeFeedback: true);
        action.SuccessText = null;
        FlowDefinitionValidator.Validate(CreateSingleActionFlow(action));
    },
    "actions[0].successText é obrigatório",
    "a confirmação final rejeita feedback parcial");
ExpectInvalid(
    () =>
    {
        var action = CreateSafeFinalConfirmation(includeFeedback: true);
        action.ProtocolPattern = @"#(\d+)";
        FlowDefinitionValidator.Validate(CreateSingleActionFlow(action));
    },
    "grupo nomeado 'protocol'",
    "a confirmação final exige o grupo nomeado do protocolo");
ExpectInvalid(
    () =>
    {
        var action = CreateSafeFinalConfirmation(includeFeedback: true);
        action.ProtocolTarget = action.CompletionTarget;
        FlowDefinitionValidator.Validate(CreateSingleActionFlow(action));
    },
    "devem ser destinos diferentes",
    "a confirmação final rejeita destinos de feedback repetidos");

var declaredRequirementsFlow = CreateSingleActionFlow(
    CreateSafeFinalConfirmation(includeFeedback: false));
declaredRequirementsFlow.Inputs =
[
    new FlowInputRequirementDefinition
    {
        Path = "input.caso",
        Type = "object"
    },
    new FlowInputRequirementDefinition
    {
        Path = "attachments.documento",
        Type = "string"
    }
];
FlowDefinitionValidator.Validate(declaredRequirementsFlow);
Check(true, "requisitos declarados aceitam input e attachments");
var requirementsRequest = new FlowExecutionRequest(
    "requisitos-de-caso",
    new JsonObject { ["caso"] = new JsonObject() },
    new JsonObject(),
    new JsonObject { ["documento"] = "C:\\entrada\\documento.pdf" });
FlowInputValidator.Validate(
    declaredRequirementsFlow.Inputs,
    new FlowDataContext(requirementsRequest));
Check(true, "o preflight resolve requisitos em input e attachments");

ExpectInvalid(
    () => FlowInputValidator.Validate(
        declaredRequirementsFlow.Inputs,
        new FlowDataContext(requirementsRequest with
        {
            Attachments = new JsonObject()
        })),
    "Entrada obrigatória ausente: 'attachments.documento'",
    "o preflight rejeita anexo obrigatório ausente");

ExpectInvalid(
    () =>
    {
        var invalidRequirementsFlow = CreateSingleActionFlow(
            CreateSafeFinalConfirmation(includeFeedback: false));
        invalidRequirementsFlow.Inputs =
        [
            new FlowInputRequirementDefinition
            {
                Path = "config.documento",
                Type = "string"
            }
        ];
        FlowDefinitionValidator.Validate(invalidRequirementsFlow);
    },
    "input.<caminho> ou attachments.<caminho>",
    "requisitos declarados rejeitam raízes que não pertencem ao caso");

ExpectInvalid(
    () =>
    {
        var invalidRequirementsFlow = CreateSingleActionFlow(
            CreateSafeFinalConfirmation(includeFeedback: false));
        invalidRequirementsFlow.Inputs =
        [
            new FlowInputRequirementDefinition
            {
                Path = "attachments..documento",
                Type = "string"
            }
        ];
        FlowDefinitionValidator.Validate(invalidRequirementsFlow);
    },
    "input.<caminho> ou attachments.<caminho>",
    "requisitos declarados rejeitam caminhos de anexo malformados");

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
var ignoredLines = ignored.Split(
    ['\r', '\n'],
    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
Check(ignoredLines.Contains("/storage/", StringComparer.Ordinal),
    "sessões e artefatos do worker na raiz estão ignorados");
Check(
    File.Exists(Path.Combine(
        repositoryRoot,
        "src",
        "Rpa.Worker",
        "Storage",
        "WorkerArtifactMaterializer.cs")),
    "o código-fonte de storage do worker está presente");

var markdownLinkPattern = new Regex(
    @"!?\[[^\]]*\]\(\s*(?<target><[^>]+>|[^)\s]+)(?:\s+[""'][^)]*[""'])?\s*\)",
    RegexOptions.CultureInvariant,
    TimeSpan.FromSeconds(1));
var missingLocalLinks = new List<string>();
foreach (var file in Directory.EnumerateFiles(
             repositoryRoot,
             "*.md",
             SearchOption.AllDirectories)
         .Where(file => !IsIgnoredPath(repositoryRoot, file)))
{
    var relativeFile = Path.GetRelativePath(repositoryRoot, file);
    var content = ReadStrict(file);
    foreach (Match match in markdownLinkPattern.Matches(content))
    {
        var target = match.Groups["target"].Value.Trim().Trim('<', '>');
        if (target.StartsWith('#') ||
            target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var fragmentIndex = target.IndexOf('#', StringComparison.Ordinal);
        if (fragmentIndex >= 0)
        {
            target = target[..fragmentIndex];
        }

        var queryIndex = target.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            target = target[..queryIndex];
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            continue;
        }

        var decodedTarget = Uri.UnescapeDataString(target)
            .Replace('/', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(file)!,
            decodedTarget));
        if (!IsInsideRepository(repositoryRoot, resolved) ||
            (!File.Exists(resolved) && !Directory.Exists(resolved)))
        {
            missingLocalLinks.Add($"{relativeFile} → {target}");
        }
    }
}

Check(
    missingLocalLinks.Count == 0,
    "todos os links locais da documentação apontam para alvos existentes",
    missingLocalLinks);

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

static FlowDefinition CreateSingleActionFlow(FlowActionDefinition action) =>
    new()
    {
        SchemaVersion = 1,
        Name = "Contrato genérico da confirmação final",
        Actions = [action]
    };

static FlowActionDefinition CreateSafeFinalConfirmation(bool includeFeedback) =>
    new()
    {
        Id = "confirmar-operacao",
        Type = "safeFinalConfirmation",
        Name = "Processar confirmação final protegida",
        Selector = "button[type='submit']",
        SuccessSelector = includeFeedback ? "p.mensagem-sucesso" : null,
        SuccessText = includeFeedback ? "Operação concluída" : null,
        ProtocolSelector = includeFeedback ? "body" : null,
        ProtocolPattern = includeFeedback ? @"#(?<protocol>\d+)" : null,
        CompletionTarget = includeFeedback ? "runtime.business.completed" : null,
        ConfirmationMessageTarget = includeFeedback
            ? "runtime.business.confirmationMessage"
            : null,
        ProtocolTarget = includeFeedback ? "runtime.business.protocol" : null,
        TimeoutMs = includeFeedback ? 60_000 : null
    };

static bool IsIgnoredPath(string repositoryRoot, string path)
{
    var segments = Path.GetRelativePath(repositoryRoot, path)
        .Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    return segments.Any(segment =>
        segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("tmp", StringComparison.OrdinalIgnoreCase));
}

static bool IsInsideRepository(string repositoryRoot, string path)
{
    var normalizedRoot = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(repositoryRoot));
    var normalizedPath = Path.GetFullPath(path);
    return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
        normalizedPath.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}

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

static void ExpectInvalid(
    Action action,
    string expectedMessage,
    string description)
{
    try
    {
        action();
    }
    catch (InvalidOperationException exception)
        when (exception.Message.Contains(expectedMessage, StringComparison.Ordinal))
    {
        Console.WriteLine($"OK: {description}.");
        return;
    }

    throw new InvalidOperationException(
        $"Falha: {description}; a validação não produziu '{expectedMessage}'.");
}
