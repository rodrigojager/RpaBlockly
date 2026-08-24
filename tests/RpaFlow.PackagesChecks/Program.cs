using System.Text.Json;
using System.Text.Json.Nodes;
using RpaFlow.Contracts.V2;
using RpaFlow.Packages;

var documents = CreateDocuments("Botão Enviar");
var validation = RpaPackageValidator.Validate(documents);
Check(validation.Warnings.Count == 0, "o pacote mínimo não possui warnings");
var oversizedFlow = CreateDocuments("Botão Enviar");
oversizedFlow.Flow.Name = new string('x', RpaPackageLimits.MaximumFlowBytes + 1);
ExpectInvalid(
    () => RpaPackageValidator.Validate(oversizedFlow),
    "documento de fluxo acima de 10 MiB é rejeitado");
var withUnusedLocator = CreateDocuments("Botão Enviar");
withUnusedLocator.Locators.Locators.Add(new LocatorDefinition
{
    Id = "unused",
    DisplayName = "Não utilizado",
    Candidates = [CreateCandidate("unused-original", "#unused", 0)]
});
Check(RpaPackageValidator.Validate(withUnusedLocator).Warnings.Count == 1,
    "locator não utilizado gera warning sem invalidar o pacote");
var withMissingReference = CreateDocuments("Botão Enviar");
withMissingReference.Flow.Actions[0].Target!.LocatorId = "missing";
ExpectInvalid(
    () => RpaPackageValidator.Validate(withMissingReference),
    "referência de locator ausente invalida o pacote");

var withOrphanSubflow = CreateDocuments("Botão Enviar");
withOrphanSubflow.Flow.Actions.Add(new FlowActionDefinition
{
    Id = "missing-subflow",
    Type = "runSubflow",
    Name = "Subfluxo ausente",
    Subflow = "not-found"
});
ExpectInvalid(
    () => RpaPackageValidator.Validate(withOrphanSubflow),
    "referência de subfluxo ausente invalida o pacote");

var withCycle = CreateDocuments("Botão Enviar");
withCycle.Flow.Subflows["a"] =
[
    new FlowActionDefinition
    {
        Id = "a-to-b",
        Type = "runSubflow",
        Name = "A chama B",
        Subflow = "b"
    }
];
withCycle.Flow.Subflows["b"] =
[
    new FlowActionDefinition
    {
        Id = "b-to-a",
        Type = "runSubflow",
        Name = "B chama A",
        Subflow = "a"
    }
];
ExpectInvalid(
    () => RpaPackageValidator.Validate(withCycle),
    "ciclo entre subfluxos invalida o pacote");

var withManyCondition = CreateDocuments("Botão Enviar");
withManyCondition.Flow.Actions.Add(new FlowActionDefinition
{
    Id = "if-many",
    Type = "if",
    Name = "Condição ambígua",
    Condition = new FlowConditionDefinition
    {
        Type = "element",
        State = "visible",
        Locator = new LocatorUseDefinition
        {
            LocatorId = "submit",
            Cardinality = LocatorCardinality.Many
        }
    },
    Actions =
    [
        new FlowActionDefinition
        {
            Id = "record-if",
            Type = "setVariable",
            Name = "Registrar",
            Value = JsonSerializer.SerializeToElement(true),
            Output = "runtime.condition"
        }
    ]
});
ExpectInvalid(
    () => RpaPackageValidator.Validate(withManyCondition),
    "condição de elemento não aceita cardinalidade many");

var withMissingFingerprint = CreateDocuments("Botão Enviar");
withMissingFingerprint.Locators.Locators[0].Candidates[0].Recipe.Target =
    new LocatorExpression
    {
        Strategy = LocatorStrategy.Fingerprint,
        FingerprintId = "not-found"
    };
ExpectInvalid(
    () => RpaPackageValidator.Validate(withMissingFingerprint),
    "candidato não referencia fingerprint órfão");

var withUnsafePolicyCombination = CreateDocuments("Botão Enviar");
withUnsafePolicyCombination.Policy.LocatorResilience.Mode =
    LocatorResilienceMode.Adaptive;
withUnsafePolicyCombination.Policy.LocatorResilience.Promotion =
    LocatorPromotionMode.AfterSuccessfulExecution;
ExpectInvalid(
    () => RpaPackageValidator.Validate(withUnsafePolicyCombination),
    "promoção sem write-back é rejeitada antes da execução");

var propertyOrderA = JsonNode.Parse("{\"b\":2,\"a\":1}")!;
var propertyOrderB = JsonNode.Parse("{\"a\":1,\"b\":2}")!;
Check(
    CanonicalJson.Serialize(propertyOrderA).SequenceEqual(
        CanonicalJson.Serialize(propertyOrderB)),
    "ordem de propriedades não altera o JSON canônico");
var arrayOrderA = JsonNode.Parse("[1,2]")!;
var arrayOrderB = JsonNode.Parse("[2,1]")!;
Check(
    !CanonicalJson.Serialize(arrayOrderA).SequenceEqual(
        CanonicalJson.Serialize(arrayOrderB)),
    "ordem de arrays permanece semanticamente significativa");

var memory = new MemoryRpaPackageStore();
var first = await memory.PublishAsync("example", documents, null, CancellationToken.None);
Check(first.CreatedNewRevision, "a primeira publicação cria revisão");
var snapshot = await memory.LoadAsync("example", null, CancellationToken.None);
Check(snapshot.Revision == first.Revision, "a leitura atual usa a revisão publicada");
snapshot.Flow.Name = "Mutação externa";
Check(snapshot.Flow.Name == "Pacote de teste",
    "o próprio snapshot não expõe estado mutável");
var unchanged = await memory.LoadAsync("example", first.Revision, CancellationToken.None);
Check(unchanged.Flow.Name == "Pacote de teste", "snapshot não compartilha mutação externa");

await ExpectConflictAsync(
    () => memory.PublishAsync(
        "example",
        CreateDocuments("Alternativa"),
        null,
        CancellationToken.None),
    "memory store exige revisão esperada ao atualizar");
var second = await memory.PublishAsync(
    "example",
    CreateDocuments("Alternativa"),
    first.Revision,
    CancellationToken.None);
Check(second.Revision != first.Revision, "conteúdo diferente cria revisão diferente");
var history = await memory.ListRevisionsAsync("example", CancellationToken.None);
Check(history.Count == 2, "memory store preserva histórico imutável");
var concurrentA = CreateDocuments("Concorrente A");
var concurrentB = CreateDocuments("Concorrente B");
var concurrentResults = await Task.WhenAll(
    TryPublishAsync(memory, "example", concurrentA, second.Revision),
    TryPublishAsync(memory, "example", concurrentB, second.Revision));
Check(concurrentResults.Count(result => result) == 1,
    "compare-and-swap permite um único vencedor concorrente");

var inline = new InlineRpaPackageSource("inline", documents);
var inlineSnapshot = await inline.LoadAsync("inline", null, CancellationToken.None);
Check(inlineSnapshot.ContentHash == first.ContentHash,
    "source inline usa o mesmo hash canônico");

var registryStore = new MemoryRpaPackageStore();
var registryFirst = await registryStore.PublishAsync(
    "registered",
    CreateDocuments("Registry v1"),
    null,
    CancellationToken.None);
var registry = new RpaPackageRuntimeRegistry(
[
    new RpaPackageRegistration(
        "registered",
        "source",
        new RpaPackageOrigin("memory", "source"),
        registryStore,
        registryStore),
    new RpaPackageRegistration(
        "registered",
        "inline",
        new RpaPackageOrigin("inline", "embedded"),
        new InlineRpaPackageSource("registered", CreateDocuments("Inline")))
]);
var registrySnapshotV1 = await registry.ResolveAsync(
    "registered",
    "source",
    null,
    CancellationToken.None);
var registrySecond = await registryStore.PublishAsync(
    "registered",
    CreateDocuments("Registry v2"),
    registryFirst.Revision,
    CancellationToken.None);
var registrySnapshotV2 = await registry.ResolveAsync(
    "registered",
    "source",
    null,
    CancellationToken.None);
var registryPinnedV1 = await registry.ResolveAsync(
    "registered",
    "source",
    registryFirst.Revision,
    CancellationToken.None);
Check(
    registrySnapshotV1.Revision == registryFirst.Revision &&
    registrySnapshotV2.Revision == registrySecond.Revision &&
    registryPinnedV1.Revision == registryFirst.Revision,
    "registry não usa TTL como consistência e mantém revisões fixadas");
Check(
    registrySnapshotV1.Locators.Locators[0].DisplayName == "Registry v1" &&
    registrySnapshotV2.Locators.Locators[0].DisplayName == "Registry v2",
    "execuções distintas podem manter snapshots de revisões diferentes");
Check(
    registry.ResolveWriter("registered", "source") == registryStore &&
    registry.ListRegistrations().Count == 2,
    "registry indexa RPA, origem, source e writer explicitamente");

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var testRoot = Path.Combine(
    repositoryRoot,
    "tmp",
    "package-checks",
    Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
try
{
    await CheckFileStoreFaultAtomicityAsync(testRoot, documents);

    var files = new FileRpaPackageStore(testRoot);
    var fileFirst = await files.PublishAsync(
        "example",
        documents,
        null,
        CancellationToken.None);
    var loaded = await files.LoadAsync("example", null, CancellationToken.None);
    Check(loaded.ContentHash == fileFirst.ContentHash,
        "file store lê os três documentos da mesma revisão");
    Check(File.Exists(Path.Combine(
        testRoot,
        "example",
        "revisions",
        fileFirst.Revision.Value,
        "flow.production.json")),
        "file store mantém revisão imutável");

    var fileSecond = await files.PublishAsync(
        "example",
        CreateDocuments("Alternativa"),
        fileFirst.Revision,
        CancellationToken.None);
    var old = await files.LoadAsync(
        "example",
        fileFirst.Revision,
        CancellationToken.None);
    Check(old.Locators.Locators[0].DisplayName == "Botão Enviar",
        "revisão anterior continua carregável");
    Check((await files.ListRevisionsAsync("example", CancellationToken.None)).Count == 2,
        "file store lista o histórico");
    await ExpectConflictAsync(
        () => files.PublishAsync(
            "example",
            documents,
            fileFirst.Revision,
            CancellationToken.None),
        "file store rejeita compare-and-swap obsoleto");
    Check((await files.LoadAsync("example", null, CancellationToken.None)).Revision ==
          fileSecond.Revision,
        "conflito não altera o ponteiro atual");
}
finally
{
    var fullTestRoot = Path.GetFullPath(testRoot);
    var allowedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "tmp", "package-checks"));
    if (!fullTestRoot.StartsWith(
            allowedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Diretório temporário escapou da área permitida.");
    }

    if (Directory.Exists(fullTestRoot))
    {
        Directory.Delete(fullTestRoot, recursive: true);
    }
}

Console.WriteLine("Package stores V2 validados com sucesso.");

static async Task CheckFileStoreFaultAtomicityAsync(
    string testRoot,
    RpaPackageDocuments initialDocuments)
{
    foreach (var stage in Enum.GetValues<FilePackageWriteStage>())
    {
        var stageRoot = Path.Combine(testRoot, "faults", stage.ToString());
        var baselineStore = new FileRpaPackageStore(stageRoot);
        var baseline = await baselineStore.PublishAsync(
            "fault-test",
            initialDocuments,
            null,
            CancellationToken.None);
        var failingStore = new FileRpaPackageStore(
            stageRoot,
            current =>
            {
                if (current == stage)
                {
                    throw new InjectedPackageWriteException(stage);
                }
            });

        try
        {
            await failingStore.PublishAsync(
                "fault-test",
                CreateDocuments($"Falha em {stage}"),
                baseline.Revision,
                CancellationToken.None);
            throw new InvalidOperationException(
                $"A falha injetada em {stage} não interrompeu a publicação.");
        }
        catch (InjectedPackageWriteException exception) when (exception.Stage == stage)
        {
        }

        var current = await baselineStore.LoadAsync(
            "fault-test",
            null,
            CancellationToken.None);
        Check(current.Revision == baseline.Revision &&
              current.Locators.Locators[0].DisplayName == "Botão Enviar",
            $"falha em {stage} preserva integralmente a revisão anterior");
    }
}

static RpaPackageDocuments CreateDocuments(string displayName) => new(
    new FlowDefinition
    {
        Name = "Pacote de teste",
        Actions =
        [
            new FlowActionDefinition
            {
                Id = "submit",
                Type = "click",
                Name = "Enviar",
                Target = new LocatorUseDefinition
                {
                    LocatorId = "submit",
                    Cardinality = LocatorCardinality.Single
                }
            }
        ]
    },
    new LocatorCatalog
    {
        Locators =
        [
            new LocatorDefinition
            {
                Id = "submit",
                DisplayName = displayName,
                Candidates =
                [
                    new LocatorCandidate
                    {
                        Id = "submit-original",
                        Origin = LocatorCandidateOrigin.Developer,
                        DeveloperRole = DeveloperLocatorRole.Original,
                        OriginalOrder = 0,
                        Recipe = new LocatorRecipe
                        {
                            Target = new LocatorExpression
                            {
                                Strategy = LocatorStrategy.Css,
                                Selector = "button[type='submit']"
                            }
                        }
                    }
                ]
            }
        ]
    },
    new RpaPolicyDefinition());

static LocatorCandidate CreateCandidate(string id, string selector, int order) => new()
{
    Id = id,
    Origin = LocatorCandidateOrigin.Developer,
    DeveloperRole = order == 0
        ? DeveloperLocatorRole.Original
        : DeveloperLocatorRole.Alternative,
    OriginalOrder = order,
    Recipe = new LocatorRecipe
    {
        Target = new LocatorExpression
        {
            Strategy = LocatorStrategy.Css,
            Selector = selector
        }
    }
};

static async Task<bool> TryPublishAsync(
    IRpaPackageWriter writer,
    string rpaId,
    RpaPackageDocuments documents,
    PackageRevision expectedRevision)
{
    try
    {
        await writer.PublishAsync(rpaId, documents, expectedRevision, CancellationToken.None);
        return true;
    }
    catch (PackageRevisionConflictException)
    {
        return false;
    }
}

static async Task ExpectConflictAsync(Func<Task> action, string description)
{
    try
    {
        await action();
    }
    catch (PackageRevisionConflictException)
    {
        Console.WriteLine($"OK: {description}.");
        return;
    }

    throw new InvalidOperationException($"Falha: {description}.");
}

static void ExpectInvalid(Action action, string description)
{
    try
    {
        action();
    }
    catch (InvalidOperationException)
    {
        Console.WriteLine($"OK: {description}.");
        return;
    }

    throw new InvalidOperationException($"Falha: {description}.");
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

file sealed class InjectedPackageWriteException(FilePackageWriteStage stage)
    : Exception($"Falha injetada em {stage}.")
{
    public FilePackageWriteStage Stage { get; } = stage;
}
