using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RpaFlow.Contracts;
using RpaFlow.Migrator;
using RpaFlow.Packages;

var source = CreateFixture();
var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
if (args.Contains("--write-baseline", StringComparer.Ordinal))
{
    WriteBaselineFixtures(repositoryRoot);
}

var first = new V1ToV2Migrator().Migrate(source, "aggregate-v1.json");
var second = new V1ToV2Migrator().Migrate(source, "aggregate-v1.json");
Check(
    CanonicalJson.ComputePackageHash(first.Documents) ==
    CanonicalJson.ComputePackageHash(second.Documents),
    "a mesma entrada V1 produz bytes semânticos determinísticos");
Check(first.Report.ActionCount == 8, "ações aninhadas entram no relatório");
Check(first.Report.LocatorCount == 12, "todos os papéis de locator são materializados");
Check(first.Report.PossibleSemanticDuplicates.Count == 1,
    "receitas iguais geram aviso e não são deduplicadas");

var select = first.Documents.Flow.Actions.Single(action => action.Id == "selecionar");
Check(
    select.Target?.LocatorId == "selecionar.target" &&
    select.Trigger?.LocatorId == "selecionar.trigger" &&
    select.Options?.LocatorId == "selecionar.options" &&
    select.Options.Cardinality.ToString() == "Many",
    "target, trigger e options recebem IDs e cardinalidades mecânicas");
var selectRecipe = first.Documents.Locators.Locators
    .Single(locator => locator.Id == "selecionar.target")
    .Candidates[0].Recipe;
Check(
    selectRecipe.Frames.Select(item => item.Selector).SequenceEqual(["#externo", "#interno"]) &&
    selectRecipe.Scope?.Selector == ".linha" &&
    selectRecipe.Scope.HasText?.Source == "input.linha" &&
    selectRecipe.Target.Selector == ".select" &&
    selectRecipe.Target.HasText?.Literal == "Tipo",
    "frames, scope e filtros literal/source preservam a ordem V1");

var collection = first.Documents.Flow.Actions.Single(action => action.Id == "ler-itens");
Check(collection.Target?.Cardinality.ToString() == "Many" &&
      collection.Output == "runtime.itens",
    "coleção usa many e target de dados V1 vira output V2");
var condition = first.Documents.Flow.Actions.Single(action => action.Id == "condicao");
Check(condition.Condition?.Locator?.LocatorId == "condicao.condition" &&
      condition.Condition.Locator.Cardinality.ToString() == "Single",
    "condição de elemento possui locator separado");
var navigation = first.Documents.Flow.Actions.Single(action => action.Id == "abrir-relatorio");
Check(navigation.Ready?.LocatorId == "abrir-relatorio.ready",
    "readySelector migra para o papel ready");
var final = first.Documents.Flow.Actions[^1];
Check(final.Success?.LocatorId == "confirmar.success" &&
      final.Protocol?.LocatorId == "confirmar.protocol" &&
      first.Report.HumanReview.Count == 1,
    "success e protocol migram com revisão humana explícita");
Check(first.Documents.Policy.LocatorResilience.Mode.ToString() == "Strict" &&
      first.Documents.Policy.LocatorResilience.LearningWriteBack.ToString() == "Disabled",
    "política migrada é strict e sem write-back");

var catalogFixture = CreateCatalogFixture();
var catalogMigration = new V1ToV2Migrator().Migrate(
    catalogFixture,
    "catalogo-completo-v1.json");
var migratedTypes = Enumerate(catalogMigration.Documents.Flow.Actions)
    .Concat(catalogMigration.Documents.Flow.Subflows.Values.SelectMany(Enumerate))
    .Select(action => action.Type)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
Check(
    migratedTypes.SetEquals(FlowActionCatalog.SupportedTypes),
    "a fixture agregada cobre mecanicamente os 32 tipos do catálogo");

ValidateVersionedBaseline(repositoryRoot);

Console.WriteLine("Migrador offline V1 → V2 validado com sucesso.");

static FlowDefinition CreateFixture() => new()
{
    SchemaVersion = 1,
    Name = "Fixture agregada sanitizada",
    Inputs = [new FlowInputRequirementDefinition { Path = "input.linha", Type = "string" }],
    Actions =
    [
        new FlowActionDefinition
        {
            Id = "selecionar",
            Type = "select2",
            Name = "Selecionar tipo",
            Selector = ".select",
            Scope = ".linha",
            ScopeHasTextSource = "input.linha",
            HasText = "Tipo",
            FrameSelectors = ["#externo", "#interno"],
            MatchMode = "single",
            TriggerSelector = ".trigger",
            OptionSelector = ".option",
            ValueSource = "input.linha"
        },
        new FlowActionDefinition
        {
            Id = "ler-itens",
            Type = "readElements",
            Name = "Ler itens",
            Selector = ".item",
            Target = "runtime.itens",
            Property = "text"
        },
        new FlowActionDefinition
        {
            Id = "condicao",
            Type = "if",
            Name = "Verificar resultado",
            Condition = new FlowConditionDefinition
            {
                Type = "element",
                Selector = ".resultado",
                MatchMode = "single",
                State = "visible"
            },
            Actions =
            [
                new FlowActionDefinition
                {
                    Id = "marcar",
                    Type = "setVariable",
                    Name = "Marcar resultado",
                    Value = JsonSerializer.SerializeToElement(true),
                    Target = "runtime.ok"
                }
            ]
        },
        new FlowActionDefinition
        {
            Id = "abrir-relatorio",
            Type = "clickAndSwitchPage",
            Name = "Abrir relatório",
            Selector = "#abrir",
            ReadySelector = "#pronto"
        },
        new FlowActionDefinition
        {
            Id = "duplicado-a",
            Type = "click",
            Name = "Duplicado A",
            Selector = "#igual"
        },
        new FlowActionDefinition
        {
            Id = "duplicado-b",
            Type = "click",
            Name = "Duplicado B",
            Selector = "#igual"
        },
        new FlowActionDefinition
        {
            Id = "confirmar",
            Type = "safeFinalConfirmation",
            Name = "Confirmar",
            Selector = "#confirmar",
            SuccessSelector = ".sucesso",
            SuccessText = "Concluído",
            ProtocolSelector = ".protocolo",
            ProtocolPattern = "(?<protocol>[A-Z]+-[0-9]+)",
            CompletionTarget = "runtime.completed",
            ConfirmationMessageTarget = "runtime.message",
            ProtocolTarget = "runtime.protocol"
        }
    ]
};

static FlowDefinition CreateCatalogFixture()
{
    var actions = FlowActionCatalog.SupportedTypes
        .Where(type => !type.Equals("safeFinalConfirmation", StringComparison.OrdinalIgnoreCase))
        .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
        .Select(CreateCatalogAction)
        .ToList();
    actions.Add(CreateCatalogAction("safeFinalConfirmation"));
    return new FlowDefinition
    {
        SchemaVersion = 1,
        Name = "Catálogo completo sanitizado",
        Actions = actions,
        Subflows = new Dictionary<string, List<FlowActionDefinition>>
        {
            ["apoio"] =
            [
                new FlowActionDefinition
                {
                    Id = "apoio-registrar",
                    Type = "setVariable",
                    Name = "Registrar apoio",
                    Value = JsonSerializer.SerializeToElement(true),
                    Target = "runtime.apoio"
                }
            ]
        }
    };
}

static FlowActionDefinition CreateCatalogAction(string type)
{
    var id = "catalogo-" + type.ToLowerInvariant();
    var action = new FlowActionDefinition
    {
        Id = id,
        Type = type,
        Name = type,
        Selector = "#alvo",
        Value = JsonSerializer.SerializeToElement("valor"),
        Target = "runtime.resultado"
    };
    switch (type.ToLowerInvariant())
    {
        case "wait":
            action.State = "visible";
            break;
        case "selectoption":
            action.OptionMode = "value";
            break;
        case "clickandswitchpage":
            action.ReadySelector = "#pronto";
            break;
        case "select2":
            action.TriggerSelector = ".trigger";
            action.OptionSelector = ".option";
            break;
        case "preserveorfill":
            action.Comparison = "exact";
            break;
        case "fillmaskedcurrency":
            action.DecimalPlaces = 2;
            break;
        case "transformpath":
            action.Operation = "fileName";
            break;
        case "waitforonetimecode":
            action.ProviderAlias = "email-otp";
            action.NotBeforeSource = "runtime.requestedAt";
            action.TimeoutMs = 120_000;
            action.PollIntervalMs = 5_000;
            break;
        case "readelement":
        case "readelements":
            action.Property = "text";
            if (type.Equals("readElements", StringComparison.OrdinalIgnoreCase))
            {
                action.MaxItems = 100;
            }
            break;
        case "switchpage":
            action.Property = "url";
            action.Comparison = "contains";
            break;
        case "download":
            action.DownloadMode = "click";
            break;
        case "screenshot":
        case "closepage":
        case "capturetimestamp":
            action.Value = default;
            if (type is "screenshot" or "closePage") action.Target = null;
            if (type.Equals("screenshot", StringComparison.OrdinalIgnoreCase))
            {
                action.Selector = null;
                action.FileName = "evidencia.png";
            }
            break;
        case "if":
            action.Selector = null;
            action.Target = null;
            action.Value = default;
            action.Condition = new FlowConditionDefinition
            {
                Type = "value",
                LeftValue = JsonSerializer.SerializeToElement(true),
                Operator = "equals",
                RightValue = JsonSerializer.SerializeToElement(true)
            };
            action.Actions =
            [
                new FlowActionDefinition
                {
                    Id = "catalogo-if-filho",
                    Type = "setVariable",
                    Name = "Filho do if",
                    Value = JsonSerializer.SerializeToElement(true),
                    Target = "runtime.ifFilho"
                }
            ];
            break;
        case "repeat":
            action.Selector = null;
            action.Target = null;
            action.Value = default;
            action.Times = 1;
            action.Actions =
            [
                new FlowActionDefinition
                {
                    Id = "catalogo-repeat-filho",
                    Type = "setVariable",
                    Name = "Filho do repeat",
                    Value = JsonSerializer.SerializeToElement(true),
                    Target = "runtime.repeatFilho"
                }
            ];
            break;
        case "foreach":
            action.Selector = null;
            action.Target = null;
            action.Value = default;
            action.Items = [JsonSerializer.SerializeToElement("item")];
            action.ItemVariable = "item";
            action.IndexVariable = "indice";
            action.Actions =
            [
                new FlowActionDefinition
                {
                    Id = "catalogo-foreach-filho",
                    Type = "setVariable",
                    Name = "Filho do forEach",
                    ValueSource = "loop.item",
                    Target = "runtime.foreachFilho"
                }
            ];
            break;
        case "runsubflow":
            action.Selector = null;
            action.Target = null;
            action.Value = default;
            action.Subflow = "apoio";
            break;
        case "safefinalconfirmation":
            action.SuccessSelector = ".sucesso";
            action.SuccessText = "Concluído";
            action.ProtocolSelector = ".protocolo";
            action.ProtocolPattern = "(?<protocol>[A-Z]+-[0-9]+)";
            action.CompletionTarget = "runtime.concluido";
            action.ConfirmationMessageTarget = "runtime.mensagem";
            action.ProtocolTarget = "runtime.protocolo";
            break;
    }
    return action;
}

static IEnumerable<RpaFlow.Contracts.V2.FlowActionDefinition> Enumerate(
    IEnumerable<RpaFlow.Contracts.V2.FlowActionDefinition> actions)
{
    foreach (var action in actions)
    {
        yield return action;
        foreach (var nested in Enumerate(action.Actions.Concat(action.ElseActions)))
        {
            yield return nested;
        }
    }
}

static void WriteBaselineFixtures(string repositoryRoot)
{
    var fixtureDirectory = Path.Combine(
        repositoryRoot,
        "tests",
        "RpaFlow.MigratorChecks",
        "Fixtures",
        "baseline-v1");
    Directory.CreateDirectory(fixtureDirectory);
    var families = ActionFamilies();
    foreach (var (family, actionTypes) in families)
    {
        WriteJson(
            Path.Combine(fixtureDirectory, $"{family}.json"),
            CreateFamilyFixture(family, actionTypes));
    }

    WriteJson(
        Path.Combine(fixtureDirectory, "aggregate-32.json"),
        CreateCatalogFixture());
    var actionToFamily = families
        .SelectMany(pair => pair.Value.Select(actionType => new
        {
            actionType,
            family = pair.Key,
            fixture = $"{pair.Key}.json"
        }))
        .OrderBy(item => item.actionType, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    WriteJson(
        Path.Combine(fixtureDirectory, "inventory.json"),
        new
        {
            schemaVersion = 1,
            baselineCommit = "03b74fe2197ad7651f4ba05ec5819efa9787f194",
            actionCount = FlowActionCatalog.SupportedTypes.Count,
            actionCoverage = actionToFamily,
            auxiliaryLocatorFields = new[]
            {
                "selector", "scope", "scopeHasText", "scopeHasTextSource",
                "hasText", "hasTextSource", "frameSelectors", "condition.selector",
                "triggerSelector", "optionSelector", "readySelector",
                "successSelector", "protocolSelector", "download.selector"
            },
            observableChecks = new[]
            {
                "RpaFlow.MigratorChecks: validação, migração e determinismo",
                "RpaFlow.EditorRoundTrip: abrir, serializar, salvar e reabrir",
                "RpaFlow.PlaywrightChecks: execução diferencial V1/V2 em páginas-fixture"
            }
        });
}

static void ValidateVersionedBaseline(string repositoryRoot)
{
    var fixtureDirectory = Path.Combine(
        repositoryRoot,
        "tests",
        "RpaFlow.MigratorChecks",
        "Fixtures",
        "baseline-v1");
    var inventory = JsonDocument.Parse(ReadStrict(Path.Combine(
        fixtureDirectory,
        "inventory.json")));
    var inventoryTypes = inventory.RootElement
        .GetProperty("actionCoverage")
        .EnumerateArray()
        .Select(item => item.GetProperty("actionType").GetString()!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    Check(
        inventoryTypes.SetEquals(FlowActionCatalog.SupportedTypes),
        "o inventário versionado acompanha exatamente o catálogo compilado");

    var coveredTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in Directory.EnumerateFiles(fixtureDirectory, "*.json")
                 .Where(path => !Path.GetFileName(path).Equals(
                     "inventory.json",
                     StringComparison.OrdinalIgnoreCase)))
    {
        var flow = FlowJsonSerializer.Deserialize(ReadStrict(path));
        FlowDefinitionValidator.Validate(flow);
        var migration = new V1ToV2Migrator().Migrate(flow, Path.GetFileName(path));
        _ = RpaPackageValidator.Validate(migration.Documents);
        coveredTypes.UnionWith(EnumerateV1(flow.Actions)
            .Concat(flow.Subflows.Values.SelectMany(EnumerateV1))
            .Select(action => action.Type));
    }

    Check(
        coveredTypes.SetEquals(FlowActionCatalog.SupportedTypes),
        "goldens V1 sanitizados por família cobrem os 32 tipos e migram para pacote válido");
}

static IReadOnlyDictionary<string, string[]> ActionFamilies() =>
    new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["navigation"] =
        [
            "navigate", "click", "clickIfVisible", "wait", "clickAndSwitchPage",
            "waitStable", "switchPage", "closePage"
        ],
        ["form"] =
        [
            "fill", "selectOption", "setChecked", "pressKey", "typeSequentially",
            "typeAcrossInputs", "upload", "preserveOrFill", "select2",
            "fillMaskedCurrency"
        ],
        ["data-artifact"] =
        [
            "fail", "transformPath", "captureTimestamp", "waitForOneTimeCode",
            "setVariable", "readElement", "readElements", "download", "screenshot",
            "safeFinalConfirmation"
        ],
        ["control"] = ["if", "repeat", "forEach", "runSubflow"]
    };

static FlowDefinition CreateFamilyFixture(string family, IEnumerable<string> actionTypes)
{
    var actions = actionTypes
        .Where(type => !type.Equals("safeFinalConfirmation", StringComparison.OrdinalIgnoreCase))
        .Select(CreateCatalogAction)
        .ToList();
    if (actionTypes.Contains("safeFinalConfirmation", StringComparer.OrdinalIgnoreCase))
    {
        actions.Add(CreateCatalogAction("safeFinalConfirmation"));
    }

    return new FlowDefinition
    {
        SchemaVersion = 1,
        Name = $"Fixture sanitizada: {family}",
        Actions = actions,
        Subflows = actionTypes.Contains("runSubflow", StringComparer.OrdinalIgnoreCase)
            ? new Dictionary<string, List<FlowActionDefinition>>
            {
                ["apoio"] =
                [
                    new FlowActionDefinition
                    {
                        Id = "apoio-registrar",
                        Type = "setVariable",
                        Name = "Registrar apoio",
                        Value = JsonSerializer.SerializeToElement(true),
                        Target = "runtime.apoio"
                    }
                ]
            }
            : []
    };
}

static IEnumerable<FlowActionDefinition> EnumerateV1(
    IEnumerable<FlowActionDefinition> actions)
{
    foreach (var action in actions)
    {
        yield return action;
        foreach (var nested in EnumerateV1(action.Actions.Concat(action.ElseActions)))
        {
            yield return nested;
        }
    }
}

static void WriteJson<T>(string path, T value)
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    var json = JsonSerializer.Serialize(value, options).ReplaceLineEndings("\n") + "\n";
    File.WriteAllText(path, json, new UTF8Encoding(false, true));
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
    if (!condition) throw new InvalidOperationException($"Falha: {description}.");
    Console.WriteLine($"OK: {description}.");
}
