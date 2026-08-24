using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using RpaFlow.Contracts;
using V2 = RpaFlow.Contracts.V2;

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var expectedTypes = new HashSet<string>(
[
    "navigate", "click", "clickIfVisible", "wait", "fill", "selectOption",
    "setChecked", "pressKey", "typeSequentially", "typeAcrossInputs",
    "clickAndSwitchPage", "upload", "waitStable", "preserveOrFill", "select2",
    "fillMaskedCurrency", "fail", "transformPath", "captureTimestamp",
    "waitForOneTimeCode", "completeAuthenticationAttempt", "setVariable",
    "readElement", "readElements",
    "switchPage", "closePage", "download", "screenshot",
    "safeFinalConfirmation", "if", "repeat", "forEach", "runSubflow"
], StringComparer.OrdinalIgnoreCase);
Check(expectedTypes.SetEquals(FlowActionCatalog.SupportedTypes),
    "a matriz V2 cobre exatamente os 33 tipos do catálogo");

foreach (var schema in new[]
         {
             "flow-v2.schema.json",
             "locators-v1.schema.json",
             "rpa-policy-v1.schema.json",
             "recorder-bundle-v1.schema.json",
             "recorder-session-v1.schema.json",
             "recorder-evidence-v1.schema.json",
             "recorder-issues-v1.schema.json",
             "recorder-integrity-v1.schema.json"
         })
{
    var path = Path.Combine(repositoryRoot, "schemas", schema);
    var bytes = await File.ReadAllBytesAsync(path);
    _ = new UTF8Encoding(false, true).GetString(bytes);
    using var document = JsonDocument.Parse(bytes);
    Check(document.RootElement.GetProperty("$schema").GetString() is not null,
        $"{schema} é JSON válido e declara draft");
}

var schemaCases = new[]
{
    new SchemaCase(
        "flow-v2.schema.json",
        Path.Combine("package-valid", "flow.production.json"),
        true),
    new SchemaCase(
        "locators-v1.schema.json",
        Path.Combine("package-valid", "locators.production.json"),
        true),
    new SchemaCase(
        "rpa-policy-v1.schema.json",
        Path.Combine("package-valid", "rpa.policy.json"),
        true),
    new SchemaCase("flow-v2.schema.json", "flow-invalid-unknown-property.json", false),
    new SchemaCase("flow-v2.schema.json", "flow-invalid-selector-embedded.json", false),
    new SchemaCase("locators-v1.schema.json", "locators-invalid-missing-target.json", false),
    new SchemaCase("locators-v1.schema.json", "locators-invalid-dual-text-source.json", false),
    new SchemaCase("locators-v1.schema.json", "locators-invalid-strategy-fields.json", false),
    new SchemaCase("rpa-policy-v1.schema.json", "policy-invalid-mode.json", false)
};
var fixturesRoot = Path.Combine(
    repositoryRoot,
    "tests",
    "RpaFlow.ContractsChecks",
    "Fixtures");
foreach (var schemaCase in schemaCases)
{
    var valid = ClassifyWithCSharp(
        schemaCase.SchemaFile,
        ReadStrict(Path.Combine(fixturesRoot, schemaCase.FixtureFile)));
    Check(
        valid == schemaCase.ExpectedValid,
        $"contrato C# classifica {schemaCase.FixtureFile} como " +
        (schemaCase.ExpectedValid ? "válido" : "inválido"));
}

var fixtureDirectory = Path.Combine(
    repositoryRoot,
    "tests",
    "RpaFlow.ContractsChecks",
    "Fixtures",
    "package-valid");
var fixtureFlow = V2.V2JsonSerializer.Deserialize<V2.FlowDefinition>(
    ReadStrict(Path.Combine(fixtureDirectory, "flow.production.json")),
    "flow fixture");
var fixtureLocators = V2.V2JsonSerializer.Deserialize<V2.LocatorCatalog>(
    ReadStrict(Path.Combine(fixtureDirectory, "locators.production.json")),
    "locators fixture");
var fixturePolicy = V2.V2JsonSerializer.Deserialize<V2.RpaPolicyDefinition>(
    ReadStrict(Path.Combine(fixtureDirectory, "rpa.policy.json")),
    "policy fixture");
V2.FlowDefinitionValidator.Validate(fixtureFlow);
V2.LocatorCatalogValidator.Validate(fixtureLocators);
V2.RpaPolicyValidator.Validate(fixturePolicy);
Check(true, "golden package V2 é aceito pelos três validadores");

var generatedContracts = ReadStrict(Path.Combine(
    repositoryRoot,
    "schemas",
    "generated",
    "contracts.ts"));
var expectedSchemaHash = ComputeSchemasHash(
    Path.Combine(repositoryRoot, "schemas"));
Check(generatedContracts.Contains(
        $"// schemas-sha256: {expectedSchemaHash}",
        StringComparison.Ordinal),
    "tipos TypeScript foram gerados da revisão atual dos schemas");
Check(expectedTypes.All(type => generatedContracts.Contains(
        $"\"{type}\"",
        StringComparison.Ordinal)),
    "tipos TypeScript contêm todo o catálogo de ações");

var flow = new V2.FlowDefinition
{
    Name = "Contrato V2 mínimo",
    Actions =
    [
        new V2.FlowActionDefinition
        {
            Id = "click-submit",
            Type = "click",
            Name = "Clicar em enviar",
            Target = new V2.LocatorUseDefinition
            {
                LocatorId = "submit",
                Cardinality = V2.LocatorCardinality.Single
            }
        }
    ]
};
V2.FlowDefinitionValidator.Validate(flow);
Check(true, "fluxo V2 mínimo é válido");

var catalog = new V2.LocatorCatalog
{
    Locators =
    [
        new V2.LocatorDefinition
        {
            Id = "submit",
            DisplayName = "Botão Enviar",
            Candidates =
            [
                new V2.LocatorCandidate
                {
                    Id = "submit-original",
                    Origin = V2.LocatorCandidateOrigin.Developer,
                    DeveloperRole = V2.DeveloperLocatorRole.Original,
                    OriginalOrder = 0,
                    Recipe = new V2.LocatorRecipe
                    {
                        Target = new V2.LocatorExpression
                        {
                            Strategy = V2.LocatorStrategy.XPath,
                            Selector = "//button[@type='submit']"
                        }
                    }
                }
            ]
        }
    ]
};
V2.LocatorCatalogValidator.Validate(catalog);
Check(true, "catálogo V2 mínimo é válido");

var policy = new V2.RpaPolicyDefinition();
V2.RpaPolicyValidator.Validate(policy);
Check(true, "política strict conservadora é válida");

var flowJson = V2.V2JsonSerializer.Serialize(flow);
var catalogJson = V2.V2JsonSerializer.Serialize(catalog);
Check(catalogJson.Contains("\"strategy\": \"xpath\"", StringComparison.Ordinal),
    "a estratégia XPath usa o valor canônico xpath");
var roundTripFlow = V2.V2JsonSerializer.Deserialize<V2.FlowDefinition>(flowJson, "flow");
var roundTripCatalog = V2.V2JsonSerializer.Deserialize<V2.LocatorCatalog>(
    catalogJson,
    "locators");
V2.FlowDefinitionValidator.Validate(roundTripFlow);
V2.LocatorCatalogValidator.Validate(roundTripCatalog);
Check(true, "documentos V2 sobrevivem ao round-trip estrito");

ExpectInvalid(
    () => V2.V2JsonSerializer.Deserialize<V2.FlowDefinition>(
        flowJson.Replace("\"name\":", "\"unknown\": true, \"name\":"),
        "flow"),
    "propriedade desconhecida é rejeitada");

var duplicate = V2.V2JsonSerializer.Deserialize<V2.FlowDefinition>(flowJson, "flow");
duplicate.Actions.Add(V2.V2JsonSerializer.Deserialize<V2.FlowDefinition>(flowJson, "flow")
    .Actions[0]);
ExpectInvalid(
    () => V2.FlowDefinitionValidator.Validate(duplicate),
    "ID de ação duplicado é rejeitado");

var manyClick = V2.V2JsonSerializer.Deserialize<V2.FlowDefinition>(flowJson, "flow");
manyClick.Actions[0].Target!.Cardinality = V2.LocatorCardinality.Many;
ExpectInvalid(
    () => V2.FlowDefinitionValidator.Validate(manyClick),
    "click com cardinalidade many é rejeitado");

var missingNavigateValue = new V2.FlowDefinition
{
    Name = "Navigate incompleto",
    Actions =
    [
        new V2.FlowActionDefinition
        {
            Id = "navigate",
            Type = "navigate",
            Name = "Abrir"
        }
    ]
};
ExpectInvalid(
    () => V2.FlowDefinitionValidator.Validate(missingNavigateValue),
    "navigate exige exatamente um valor literal ou source");

var dualFillValue = V2.V2JsonSerializer.Deserialize<V2.FlowDefinition>(flowJson, "flow");
dualFillValue.Actions[0].Type = "fill";
dualFillValue.Actions[0].Value = JsonSerializer.SerializeToElement("literal");
dualFillValue.Actions[0].ValueSource = "input.valor";
ExpectInvalid(
    () => V2.FlowDefinitionValidator.Validate(dualFillValue),
    "ação rejeita valor literal e source simultâneos");

var invalidWait = V2.V2JsonSerializer.Deserialize<V2.FlowDefinition>(flowJson, "flow");
invalidWait.Actions[0].Type = "wait";
invalidWait.Actions[0].State = "ready";
ExpectInvalid(
    () => V2.FlowDefinitionValidator.Validate(invalidWait),
    "wait rejeita estado fora do contrato");

var sensitiveCatalog = V2.V2JsonSerializer.Deserialize<V2.LocatorCatalog>(
    catalogJson,
    "locators");
sensitiveCatalog.Locators[0].Fingerprints.Add(new V2.LocatorFingerprint
{
    Id = "sensitive",
    TagName = "input",
    Attributes = new Dictionary<string, string> { ["value"] = "segredo" }
});
ExpectInvalid(
    () => V2.LocatorCatalogValidator.Validate(sensitiveCatalog),
    "fingerprint rejeita atributo sensível");

var mixedStrategyCatalog = V2.V2JsonSerializer.Deserialize<V2.LocatorCatalog>(
    catalogJson,
    "locators");
mixedStrategyCatalog.Locators[0].Candidates[0].Recipe.Target.Role = "button";
ExpectInvalid(
    () => V2.LocatorCatalogValidator.Validate(mixedStrategyCatalog),
    "estratégia de locator rejeita campos incompatíveis");

var alternativeFirstCatalog = V2.V2JsonSerializer.Deserialize<V2.LocatorCatalog>(
    catalogJson,
    "locators");
alternativeFirstCatalog.Locators[0].Candidates[0].DeveloperRole =
    V2.DeveloperLocatorRole.Alternative;
ExpectInvalid(
    () => V2.LocatorCatalogValidator.Validate(alternativeFirstCatalog),
    "primeiro candidato deve representar o principal de autoria");

Console.WriteLine("Contratos V2 validados com sucesso.");

static string ReadStrict(string path) =>
    new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));

static bool ClassifyWithCSharp(string schemaFile, string json)
{
    try
    {
        switch (schemaFile)
        {
            case "flow-v2.schema.json":
                V2.FlowDefinitionValidator.Validate(
                    V2.V2JsonSerializer.Deserialize<V2.FlowDefinition>(json, "flow fixture"));
                break;
            case "locators-v1.schema.json":
                V2.LocatorCatalogValidator.Validate(
                    V2.V2JsonSerializer.Deserialize<V2.LocatorCatalog>(json, "locators fixture"));
                break;
            case "rpa-policy-v1.schema.json":
                V2.RpaPolicyValidator.Validate(
                    V2.V2JsonSerializer.Deserialize<V2.RpaPolicyDefinition>(json, "policy fixture"));
                break;
            default:
                throw new InvalidOperationException($"Schema sem classificador C#: {schemaFile}.");
        }

        return true;
    }
    catch (Exception exception)
        when (exception is InvalidOperationException or JsonException)
    {
        return false;
    }
}

static string ComputeSchemasHash(string schemaDirectory)
{
    var paths = new[]
    {
        Path.Combine(schemaDirectory, "flow-v2.schema.json"),
        Path.Combine(schemaDirectory, "locators-v1.schema.json"),
        Path.Combine(schemaDirectory, "rpa-policy-v1.schema.json"),
        Path.Combine(schemaDirectory, "recorder-bundle-v1.schema.json"),
        Path.Combine(schemaDirectory, "recorder-session-v1.schema.json"),
        Path.Combine(schemaDirectory, "recorder-evidence-v1.schema.json"),
        Path.Combine(schemaDirectory, "recorder-issues-v1.schema.json"),
        Path.Combine(schemaDirectory, "recorder-integrity-v1.schema.json")
    };
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var path in paths.OrderBy(path => path, StringComparer.Ordinal))
    {
        hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path)));
        hash.AppendData([0]);
        hash.AppendData(File.ReadAllBytes(path));
        hash.AppendData([0]);
    }

    return Convert.ToHexString(hash.GetHashAndReset());
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

static void ExpectInvalid(Action action, string description)
{
    try
    {
        action();
    }
    catch (Exception exception)
        when (exception is InvalidOperationException or JsonException)
    {
        Console.WriteLine($"OK: {description}.");
        return;
    }

    throw new InvalidOperationException($"Falha: {description}.");
}

file sealed record SchemaCase(
    string SchemaFile,
    string FixtureFile,
    bool ExpectedValid);
