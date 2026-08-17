using System.Text;
using System.Text.Json;
using RpaFlow.Contracts.Recorder;
using RpaFlow.Contracts.V2;

var root = FindRepositoryRoot(AppContext.BaseDirectory);
var fixtures = Path.Combine(root, "tests", "RpaFlow.RecorderContractChecks", "Fixtures");

var manifest = Read<RecorderBundleManifest>("valid/manifest.json");
var session = Read<RecorderSessionDocument>("valid/session.json");
var evidence = Read<RecorderEvidenceDocument>("valid/evidence.json");
var issues = Read<RecorderIssuesDocument>("valid/issues.json");
var fixtureIntegrity = Read<RecorderIntegrityDocument>("valid/integrity.json");
RecorderContractValidator.Validate(manifest);
RecorderContractValidator.Validate(session);
RecorderContractValidator.Validate(evidence);
RecorderContractValidator.Validate(issues);
RecorderContractValidator.Validate(fixtureIntegrity);
Check(true, "fixtures Recorder válidas são aceitas pelos contratos C#");

ExpectFixtureInvalid<RecorderBundleManifest>("invalid/manifest-replay.json",
    RecorderContractValidator.Validate, "bundle com replay é rejeitado");
ExpectFixtureInvalid<RecorderSessionDocument>("invalid/session-state.json",
    RecorderContractValidator.Validate, "estado de sessão desconhecido é rejeitado");
ExpectFixtureInvalid<RecorderEvidenceDocument>("invalid/evidence-path.json",
    RecorderContractValidator.Validate, "evidência com path traversal é rejeitada");
ExpectFixtureInvalid<RecorderIssuesDocument>("invalid/issue-code.json",
    RecorderContractValidator.Validate, "código de issue desconhecido é rejeitado");
ExpectFixtureInvalid<RecorderIntegrityDocument>("invalid/integrity-path.json",
    RecorderContractValidator.Validate, "integridade com path traversal é rejeitada");

var recorderCatalog = new LocatorCatalog
{
    Locators =
    [
        new LocatorDefinition
        {
            Id = "form.name",
            DisplayName = "Nome",
            Candidates =
            [
                new LocatorCandidate
                {
                    Id = "form-name-testid",
                    Origin = LocatorCandidateOrigin.Recorder,
                    RecorderRole = RecorderLocatorRole.CapturedPrimary,
                    OriginalOrder = 0,
                    Recipe = new LocatorRecipe
                    {
                        Target = new LocatorExpression
                        {
                            Strategy = LocatorStrategy.TestId,
                            Text = "name"
                        }
                    }
                }
            ]
        }
    ]
};
LocatorCatalogValidator.Validate(recorderCatalog);
Check(true, "candidato Recorder principal é aceito sem developerRole");
recorderCatalog.Locators[0].Candidates[0].DeveloperRole = DeveloperLocatorRole.Original;
ExpectInvalid(() => LocatorCatalogValidator.Validate(recorderCatalog),
    "origem Recorder não pode falsificar autoria de desenvolvedor");

var integrity = new RecorderIntegrityDocument
{
    Entries =
    [
        new RecorderIntegrityEntry
        {
            Path = "package/flow.production.json",
            Sha256 = new string('A', 64),
            Size = 100
        }
    ]
};
RecorderContractValidator.Validate(integrity);
integrity.Entries.Add(new RecorderIntegrityEntry
{
    Path = "PACKAGE/flow.production.json",
    Sha256 = new string('B', 64),
    Size = 100
});
ExpectInvalid(() => RecorderContractValidator.Validate(integrity),
    "integridade rejeita nomes duplicados sem diferença de caixa");
ExpectInvalid(() => RecorderContractValidator.ValidateRelativePath("../escape.json"),
    "path traversal é rejeitado antes de ler o ZIP");

Console.WriteLine("Contratos Recorder validados com sucesso.");

T Read<T>(string relative) where T : class =>
    V2JsonSerializer.Deserialize<T>(
        new UTF8Encoding(false, true).GetString(
            File.ReadAllBytes(Path.Combine(fixtures, relative.Replace('/', Path.DirectorySeparatorChar)))),
        relative);

void ExpectFixtureInvalid<T>(string relative, Action<T> validator, string description)
    where T : class
{
    ExpectInvalid(() => validator(Read<T>(relative)), description);
}

static void ExpectInvalid(Action action, string description)
{
    try
    {
        action();
    }
    catch (Exception exception) when (exception is InvalidOperationException or JsonException)
    {
        Console.WriteLine($"OK: {description}.");
        return;
    }

    throw new InvalidOperationException($"Falha: {description}.");
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException($"Falha: {description}.");
    Console.WriteLine($"OK: {description}.");
}

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(Path.GetFullPath(start));
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "RpaBlockly.slnx")))
            return directory.FullName;
        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
}
