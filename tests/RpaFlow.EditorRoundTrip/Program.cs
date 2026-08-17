using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using RpaFlow.Contracts.Recorder;
using RpaFlow.Contracts.V2;
using RpaFlow.Packages;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Uso: RpaFlow.EditorRoundTrip <RpaFlow.Editor.dll> <raiz-do-workspace>.");
    return 2;
}

var editorDll = Path.GetFullPath(args[0]);
var repositoryRoot = Path.GetFullPath(args[1]);
var allowedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "tmp", "editor-v2-checks"));
var testRoot = Path.Combine(allowedRoot, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
try
{
    await PrepareProjectAsync(repositoryRoot, testRoot);
    var port = FindAvailablePort();
    var editorUrl = $"http://127.0.0.1:{port}";
    using var editor = StartEditor(editorDll, testRoot, editorUrl);
    try
    {
        await WaitForEditorAsync(editorUrl, editor);
        await CheckBrowserRoundTripAsync(editorUrl, testRoot);
        await CheckAtomicPackageApiAsync(editorUrl);
        await CheckRecorderImportApiAsync(editorUrl, testRoot);
    }
    finally
    {
        if (!editor.HasExited)
        {
            editor.Kill(entireProcessTree: true);
            await editor.WaitForExitAsync();
        }
    }
}
finally
{
    var fullTestRoot = Path.GetFullPath(testRoot);
    if (!fullTestRoot.StartsWith(
            allowedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("A pasta temporária escapou da raiz permitida.");
    }
    if (Directory.Exists(fullTestRoot)) Directory.Delete(fullTestRoot, recursive: true);
}

Console.WriteLine("Editor Blockly V2 e persistência de pacote validados com sucesso.");
return 0;

static async Task PrepareProjectAsync(string repositoryRoot, string testRoot)
{
    var source = new FileRpaPackageStore(
        Path.Combine(repositoryRoot, "examples", "RpaExemplo", "package-store"));
    var snapshot = await source.LoadAsync("rpa-exemplo", null, CancellationToken.None);
    var target = new FileRpaPackageStore(Path.Combine(testRoot, "package-store"));
    _ = await target.PublishAsync(
        "editor-test",
        snapshot.CopyDocuments(),
        null,
        CancellationToken.None);
    var utf8 = new UTF8Encoding(false, true);
    await File.WriteAllTextAsync(
        Path.Combine(testRoot, "EditorTest.csproj"),
        "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
        utf8);
    await File.WriteAllTextAsync(Path.Combine(testRoot, "appsettings.json"), "{}\n", utf8);
    await File.WriteAllTextAsync(
        Path.Combine(testRoot, "rpa.editor.json"),
        """
        {
          "displayName": "Editor V2 em teste",
          "projectFile": "EditorTest.csproj",
          "configurationFile": "appsettings.json",
          "rpaId": "editor-test",
          "packageStoreRoot": "package-store",
          "configurationFields": []
        }
        """.ReplaceLineEndings("\n") + "\n",
        utf8);
}

static async Task CheckBrowserRoundTripAsync(string editorUrl, string testRoot)
{
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(
        new BrowserTypeLaunchOptions { Headless = true });
    var page = await browser.NewPageAsync();
    await page.GotoAsync(
        $"{editorUrl}/?roundtrip-test=1",
        new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await page.WaitForFunctionAsync("() => Boolean(window.RpaFlowEditorTesting)");

    var snapshot = await new FileRpaPackageStore(Path.Combine(testRoot, "package-store"))
        .LoadAsync("editor-test", null, CancellationToken.None);
    var packageJson = JsonSerializer.Serialize(
        new
        {
            flow = snapshot.Flow,
            locators = snapshot.Locators,
            policy = snapshot.Policy
        },
        V2JsonSerializer.WriteOptions);
    var roundTripJson = await page.EvaluateAsync<string>(
        """
        json => {
          const value = JSON.parse(json);
          return JSON.stringify(window.RpaFlowEditorTesting.roundTrip(
          value.flow, value.locators, value.policy))
        }
        """,
        packageJson);
    var roundTrip = V2JsonSerializer.Deserialize<FlowDefinition>(
        roundTripJson,
        "round-trip do editor");
    Check(
        V2JsonSerializer.Serialize(roundTrip) == V2JsonSerializer.Serialize(snapshot.Flow),
        "abrir e exportar preserva a semântica do fluxo V2");

    var blocksJson = await page.EvaluateAsync<string>(
        "() => JSON.stringify(window.RpaFlowEditorTesting.instantiateAllBlocks())");
    var blocks = JsonSerializer.Deserialize<BlockInspection[]>(
        blocksJson,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    Check(blocks.Length == 35 && blocks.Select(item => item.Type).Distinct().Count() == 35,
        "a toolbox V2 instancia os 35 blocos do baseline");
    Check(blocks.SelectMany(item => item.Fields).All(field =>
        !field.Contains("SELECTOR", StringComparison.OrdinalIgnoreCase) &&
        !field.Equals("SCOPE", StringComparison.OrdinalIgnoreCase)),
        "nenhum bloco V2 armazena campo de seletor ou scope");

    var uiLocators = new LocatorCatalog
    {
        Locators =
        [
            LocatorForUi("primary", "Botão principal", "#primary"),
            LocatorForUi("secondary", "Botão secundário", "#secondary")
        ]
    };
    var uiPackageJson = JsonSerializer.Serialize(
        new
        {
            flow = snapshot.Flow,
            locators = uiLocators,
            policy = snapshot.Policy
        },
        V2JsonSerializer.WriteOptions);
    await page.EvaluateAsync(
        """
        json => {
          const value = JSON.parse(json);
          window.RpaFlowEditorTesting.setPackage(
            value.flow, value.locators, value.policy);
        }
        """,
        uiPackageJson);
    await page.FillAsync("#locator-search", "secundário");
    var visibleLocatorIds = await page.Locator("#locator-list [data-locator-id]")
        .EvaluateAllAsync<string[]>(
            "items => items.map(item => item.dataset.locatorId)");
    Check(visibleLocatorIds.SequenceEqual(["secondary"]),
        "drawer de locators pesquisa por nome amigável e ID");

    await page.EvaluateAsync("() => window.RpaFlowEditorTesting.openLocatorPicker()");
    await page.FillAsync("dialog.locator-picker input[type=search]", "secondary");
    var pickerButtons = page.Locator("dialog.locator-picker .locator-list-item");
    Check(await pickerButtons.CountAsync() == 1,
        "FieldLocatorReference filtra opções sem expor receita no bloco");
    await pickerButtons.ClickAsync();
    var pickerValue = await page.EvaluateAsync<string>(
        "() => window.RpaFlowEditorTesting.locatorPickerValue()");
    Check(pickerValue == "secondary",
        "FieldLocatorReference persiste somente o locatorId escolhido");

    var policyJson = await page.InputValueAsync("#policy-json");
    var policyDraft = JsonNode.Parse(policyJson)!.AsObject();
    policyDraft["locatorResilience"]!["mode"] = "fallback";
    await page.FillAsync("#policy-json", policyDraft.ToJsonString(
        new JsonSerializerOptions { WriteIndented = true }));
    await page.ClickAsync("#save-policy-draft");
    var appliedPolicy = JsonNode.Parse(await page.EvaluateAsync<string>(
        "() => JSON.stringify(window.RpaFlowEditorTesting.packagePolicy())"))!.AsObject();
    Check(appliedPolicy["locatorResilience"]?["mode"]?.GetValue<string>() == "fallback",
        "drawer de policy valida e aplica a política ao rascunho");

    await page.ClickAsync("#open-recorder-import");
    Check(await page.Locator("#recorder-import-dialog").GetAttributeAsync("open") is not null,
        "wizard Recorder abre dentro do editor real");
    Check(await page.Locator("[data-recorder-step]").CountAsync() == 5,
        "wizard expõe selecionar, revisar, mapear, confirmar e aplicar");
    await page.ClickAsync("#close-recorder-import");
}

static LocatorDefinition LocatorForUi(string id, string displayName, string selector) =>
    new()
    {
        Id = id,
        DisplayName = displayName,
        Candidates =
        [
            new LocatorCandidate
            {
                Id = id + ".original",
                Origin = LocatorCandidateOrigin.Developer,
                DeveloperRole = DeveloperLocatorRole.Original,
                OriginalOrder = 0,
                Recipe = new LocatorRecipe
                {
                    Target = new LocatorExpression
                    {
                        Strategy = LocatorStrategy.Css,
                        Selector = selector
                    }
                }
            }
        ]
    };

static async Task CheckAtomicPackageApiAsync(string editorUrl)
{
    using var unauthorizedClient = new HttpClient { BaseAddress = new Uri(editorUrl) };
    using var unauthorized = await unauthorizedClient.GetAsync("/api/flow");
    Check(unauthorized.StatusCode == HttpStatusCode.Unauthorized,
        "APIs de componente exigem o token local da sessão");

    using var client = new HttpClient { BaseAddress = new Uri(editorUrl) };
    var session = await client.GetFromJsonAsync<JsonObject>("/api/session")
        ?? throw new InvalidOperationException("Sessão do editor vazia.");
    client.DefaultRequestHeaders.Add(
        "X-Editor-Token",
        session["token"]!.GetValue<string>());
    var opened = await client.GetFromJsonAsync<JsonObject>("/api/package")
        ?? throw new InvalidOperationException("Pacote do editor vazio.");
    var revision = opened["revision"]!.GetValue<string>();
    var changedFlow = opened["flow"]!.DeepClone().AsObject();
    changedFlow["name"] = "Revisão salva pelo editor";
    var request = new JsonObject
    {
        ["expectedRevision"] = revision,
        ["flow"] = changedFlow,
        ["locators"] = opened["locators"]!.DeepClone(),
        ["policy"] = opened["policy"]!.DeepClone()
    };
    using var saved = await client.PutAsJsonAsync("/api/package", request);
    if (!saved.IsSuccessStatusCode)
    {
        throw new InvalidOperationException(
            $"Falha ao salvar pacote ({(int)saved.StatusCode}): " +
            await saved.Content.ReadAsStringAsync());
    }
    Check(true, "save publica atomicamente os três documentos");
    var savedPackage = JsonNode.Parse(await saved.Content.ReadAsStringAsync())!.AsObject();
    Check(savedPackage["revision"]!.GetValue<string>() != revision,
        "alteração semântica cria uma revisão nova");

    changedFlow["name"] = "Tentativa obsoleta";
    using var conflict = await client.PutAsJsonAsync("/api/package", request);
    Check(conflict.StatusCode == HttpStatusCode.Conflict,
        "compare-and-swap recusa revisão esperada obsoleta");
    var reopened = await client.GetFromJsonAsync<JsonObject>("/api/package")
        ?? throw new InvalidOperationException("Pacote reaberto vazio.");
    Check(reopened["flow"]?["name"]?.GetValue<string>() == "Revisão salva pelo editor",
        "conflito não altera a revisão publicada");

    var flowComponent = await client.GetFromJsonAsync<JsonObject>("/api/flow")
        ?? throw new InvalidOperationException("Componente flow vazio.");
    var componentRevision = flowComponent["revision"]!.GetValue<string>();
    var componentFlow = flowComponent["document"]!.DeepClone().AsObject();
    componentFlow["name"] = "Fluxo salvo pela API de componente";
    using var flowSaved = await client.PutAsJsonAsync(
        "/api/flow",
        new JsonObject
        {
            ["expectedRevision"] = componentRevision,
            ["document"] = componentFlow.DeepClone()
        });
    Check(flowSaved.IsSuccessStatusCode,
        "API de flow publica uma revisão completa do pacote");
    var afterFlow = JsonNode.Parse(await flowSaved.Content.ReadAsStringAsync())!.AsObject();
    var afterFlowRevision = afterFlow["revision"]!.GetValue<string>();
    Check(
        afterFlow["locators"] is not null && afterFlow["policy"] is not null,
        "save de flow preserva e devolve locators e policy da mesma revisão");

    var locatorComponent = await client.GetFromJsonAsync<JsonObject>("/api/locators")
        ?? throw new InvalidOperationException("Componente locators vazio.");
    var locatorDocument = locatorComponent["document"]!.DeepClone().AsObject();
    locatorDocument["locators"]!.AsArray().Add(JsonNode.Parse(
        """
        {
          "id": "editor-added",
          "displayName": "Botão atualizado",
          "candidates": [
            {
              "id": "editor-added.original",
              "origin": "developer",
              "developerRole": "original",
              "originalOrder": 0,
              "recipe": {
                "frames": [],
                "target": {
                  "strategy": "css",
                  "selector": "#editor-added"
                }
              }
            }
          ],
          "fingerprints": []
        }
        """));
    using var locatorsSaved = await client.PutAsJsonAsync(
        "/api/locators",
        new JsonObject
        {
            ["expectedRevision"] = afterFlowRevision,
            ["document"] = locatorDocument
        });
    Check(locatorsSaved.IsSuccessStatusCode,
        "API de locators publica sem duplicar receitas no fluxo");
    var afterLocators = JsonNode.Parse(
        await locatorsSaved.Content.ReadAsStringAsync())!.AsObject();
    var afterLocatorsRevision = afterLocators["revision"]!.GetValue<string>();

    var policyComponent = await client.GetFromJsonAsync<JsonObject>("/api/policy")
        ?? throw new InvalidOperationException("Componente policy vazio.");
    var policyDocument = policyComponent["document"]!.DeepClone().AsObject();
    policyDocument["locatorResilience"]!["mode"] = "fallback";
    using var policySaved = await client.PutAsJsonAsync(
        "/api/policy",
        new JsonObject
        {
            ["expectedRevision"] = afterLocatorsRevision,
            ["document"] = policyDocument
        });
    Check(policySaved.IsSuccessStatusCode,
        "API de policy publica a política validada como parte do pacote");

    using var staleComponent = await client.PutAsJsonAsync(
        "/api/flow",
        new JsonObject
        {
            ["expectedRevision"] = componentRevision,
            ["document"] = componentFlow
        });
    Check(staleComponent.StatusCode == HttpStatusCode.Conflict,
        "API de componente também recusa revisão obsoleta");

    var invalidLocators = locatorDocument.DeepClone().AsObject();
    invalidLocators["locators"]!.AsArray()[0]!["candidates"] = new JsonArray();
    var current = JsonNode.Parse(await policySaved.Content.ReadAsStringAsync())!.AsObject();
    using var invalidComponent = await client.PutAsJsonAsync(
        "/api/locators",
        new JsonObject
        {
            ["expectedRevision"] = current["revision"]!.GetValue<string>(),
            ["document"] = invalidLocators
        });
    Check(invalidComponent.StatusCode == HttpStatusCode.BadRequest,
        "API de componente recusa conjunto cruzado inconsistente");
}

static async Task CheckRecorderImportApiAsync(string editorUrl, string testRoot)
{
    using var client = new HttpClient { BaseAddress = new Uri(editorUrl) };
    var session = await client.GetFromJsonAsync<JsonObject>("/api/session")
        ?? throw new InvalidOperationException("Sessão do editor vazia.");
    client.DefaultRequestHeaders.Add(
        "X-Editor-Token",
        session["token"]!.GetValue<string>());
    var store = new FileRpaPackageStore(Path.Combine(testRoot, "package-store"));
    var imported = await store.LoadAsync("editor-test", null, CancellationToken.None);
    var importedDocuments = imported.CopyDocuments();
    importedDocuments.Flow.Name = "Fluxo substituído pelo Recorder";
    var bundle = BuildRecorderBundle(importedDocuments, "bundle-editor-valid-001");

    using (var unauthorized = new HttpClient { BaseAddress = new Uri(editorUrl) })
    using (var content = new ByteArrayContent(bundle))
    using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/recorder/imports/inspect")
           { Content = content })
    {
        request.Headers.Add("X-File-Name", "fixture.rpablockly.zip");
        using var response = await unauthorized.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.Unauthorized,
            "inspect Recorder exige token local do editor");
    }

    var beforeInspect = (await store.LoadAsync("editor-test", null, CancellationToken.None))
        .Revision.Value;
    var inspection = await InspectRecorderAsync(client, bundle);
    var afterInspect = (await store.LoadAsync("editor-test", null, CancellationToken.None))
        .Revision.Value;
    Check(beforeInspect == afterInspect,
        "inspect valida o bundle sem alterar a revisão do pacote");
    Check(inspection.Preview["bundleId"]?.GetValue<string>() == "bundle-editor-valid-001",
        "preview expõe identidade e proveniência do bundle");

    var staleValidation = await PostRecorderDecisionAsync(
        client,
        inspection.StagingId,
        inspection.StagingToken,
        "validate",
        RecorderDecision(new string('0', 64), "replace"));
    Check(staleValidation["canApply"]?.GetValue<bool>() == false,
        "importação recusa revisão esperada obsoleta antes do apply");

    var replace = RecorderDecision(beforeInspect, "replace");
    var validation = await PostRecorderDecisionAsync(
        client,
        inspection.StagingId,
        inspection.StagingToken,
        "validate",
        replace);
    Check(validation["canApply"]?.GetValue<bool>() == true,
        "validate monta o replace em memória sem publicar");
    Check((await store.LoadAsync("editor-test", null, CancellationToken.None)).Revision.Value ==
          beforeInspect,
        "validate Recorder permanece somente leitura");

    var replaced = await PostRecorderDecisionAsync(
        client,
        inspection.StagingId,
        inspection.StagingToken,
        "apply",
        replace);
    var replaceRevision = replaced["revision"]!.GetValue<string>();
    Check(replaceRevision != beforeInspect &&
          replaced["flow"]?["name"]?.GetValue<string>() == "Fluxo substituído pelo Recorder",
        "replace publica e reabre a revisão importada");
    Check(replaced["evidenceArchive"]?.GetValue<string>().Contains(
              ".recorder-imports", StringComparison.Ordinal) == true,
        "apply preserva o bundle e mappings como evidência lateral");
    var replay = await PostRecorderDecisionAsync(
        client,
        inspection.StagingId,
        inspection.StagingToken,
        "apply",
        replace);
    Check(replay["idempotentReplay"]?.GetValue<bool>() == true &&
          replay["revision"]?.GetValue<string>() == replaceRevision,
        "repetição do mesmo apply é idempotente");

    var appendInspection = await InspectRecorderAsync(client, bundle);
    var append = RecorderDecision(replaceRevision, "appendMain", remap: true);
    var appended = await PostRecorderDecisionAsync(
        client,
        appendInspection.StagingId,
        appendInspection.StagingToken,
        "apply",
        append);
    var appendRevision = appended["revision"]!.GetValue<string>();
    Check(appended["flow"]?["actions"]?.AsArray().Count ==
          importedDocuments.Flow.Actions.Count * 2,
        "append acrescenta ações ao principal com IDs remapeados");
    Check(appended["idRemappings"]?.AsObject().Count > 0,
        "colisões de append nunca são resolvidas silenciosamente");

    var subflowInspection = await InspectRecorderAsync(client, bundle);
    var subflow = RecorderDecision(
        appendRevision,
        "subflow",
        remap: true,
        subflowName: "fluxoGravado");
    var subflowApplied = await PostRecorderDecisionAsync(
        client,
        subflowInspection.StagingId,
        subflowInspection.StagingToken,
        "apply",
        subflow);
    Check(subflowApplied["flow"]?["subflows"]?["fluxoGravado"] is JsonArray,
        "modo subflow preserva o principal e adiciona definição explícita");

    await CheckRecorderSecretMappingAsync(
        client,
        importedDocuments,
        subflowApplied["revision"]!.GetValue<string>());

    await RejectMaliciousRecorderBundlesAsync(client, importedDocuments);

    using var delete = new HttpRequestMessage(
        HttpMethod.Delete,
        $"/api/recorder/imports/{subflowInspection.StagingId}");
    delete.Headers.Add("X-Recorder-Staging-Token", subflowInspection.StagingToken);
    using var deleted = await client.SendAsync(delete);
    Check(deleted.StatusCode == HttpStatusCode.NoContent,
        "cancelamento remove staging de forma idempotente");
}

static async Task CheckRecorderSecretMappingAsync(
    HttpClient client,
    RpaPackageDocuments source,
    string currentRevision)
{
    var documents = new RpaPackageDocuments(
        V2JsonSerializer.Deserialize<FlowDefinition>(
            V2JsonSerializer.Serialize(source.Flow), "secret flow"),
        V2JsonSerializer.Deserialize<LocatorCatalog>(
            V2JsonSerializer.Serialize(source.Locators), "secret locators"),
        V2JsonSerializer.Deserialize<RpaPolicyDefinition>(
            V2JsonSerializer.Serialize(source.Policy), "secret policy"));
    documents = documents with { Flow = new FlowDefinition
    {
        Name = "Fluxo com segredo Recorder",
        Actions =
        [
            new FlowActionDefinition
            {
                Id = "fill-recorder-secret",
                Type = "fill",
                Name = "Preencher segredo importado",
                Target = new LocatorUseDefinition
                {
                    LocatorId = documents.Locators.Locators[0].Id,
                    Cardinality = LocatorCardinality.Single
                },
                ValueSource = "secret.recorded.password"
            }
        ]
    } };
    var bundle = ZipEntries(CreateRecorderEntries(
        documents,
        "bundle-editor-secret-001",
        includeSecret: true));
    var inspection = await InspectRecorderAsync(client, bundle);
    var missingMapping = RecorderDecision(currentRevision, "replace");
    var rejected = await PostRecorderDecisionAsync(
        client,
        inspection.StagingId,
        inspection.StagingToken,
        "validate",
        missingMapping);
    Check(rejected["canApply"]?.GetValue<bool>() == false,
        "bundle com segredo não pode ser publicado sem mapping explícito");
    missingMapping["secretMappings"] = new JsonObject
    {
        ["secret.recorded.password"] = "config.password"
    };
    var accepted = await PostRecorderDecisionAsync(
        client,
        inspection.StagingId,
        inspection.StagingToken,
        "validate",
        missingMapping);
    Check(accepted["canApply"]?.GetValue<bool>() == true,
        "mapping backend remove secret.recorded antes da publicação");
}

static async Task RejectMaliciousRecorderBundlesAsync(
    HttpClient client,
    RpaPackageDocuments documents)
{
    var baseEntries = CreateRecorderEntries(documents, "bundle-malicious-base");

    var tampered = new Dictionary<string, byte[]>(baseEntries, StringComparer.Ordinal)
    {
        ["package/flow.production.json"] = Encoding.UTF8.GetBytes("{\"adulterado\":true}\n")
    };
    await ExpectInspectRejectedAsync(client, ZipEntries(tampered),
        "hash adulterado é rejeitado antes de desserializar JSON");

    var zipSlip = new Dictionary<string, byte[]>(baseEntries, StringComparer.Ordinal)
    {
        ["../escape.json"] = Encoding.UTF8.GetBytes("{}")
    };
    await ExpectInspectRejectedAsync(client, ZipEntries(zipSlip),
        "Zip Slip é rejeitado sem extração");

    var duplicate = ZipEntries(baseEntries, archive =>
    {
        var entry = archive.CreateEntry("MANIFEST.json", CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(baseEntries["manifest.json"]);
    });
    await ExpectInspectRejectedAsync(client, duplicate,
        "nomes duplicados sem diferença de caixa são rejeitados");

    var symlink = ZipEntries(baseEntries, archive =>
    {
        var entry = archive.CreateEntry("samples/uploads/link", CompressionLevel.NoCompression);
        entry.ExternalAttributes = unchecked((int)0xA0000000u);
        using var stream = entry.Open();
        stream.WriteByte(0);
    });
    await ExpectInspectRejectedAsync(client, symlink,
        "entrada marcada como symlink é rejeitada");

    var bomb = new Dictionary<string, byte[]>(baseEntries, StringComparer.Ordinal)
    {
        ["samples/uploads/bomb.bin"] = new byte[2 * 1024 * 1024]
    };
    await ExpectInspectRejectedAsync(client, ZipEntries(bomb),
        "razão de compressão de Zip Bomb é rejeitada");
}

static async Task ExpectInspectRejectedAsync(
    HttpClient client,
    byte[] bytes,
    string description)
{
    using var content = new ByteArrayContent(bytes);
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/recorder/imports/inspect")
    {
        Content = content
    };
    request.Headers.Add("X-File-Name", "malicious.rpablockly.zip");
    using var response = await client.SendAsync(request);
    Check(response.StatusCode == HttpStatusCode.BadRequest, description);
}

static async Task<RecorderInspection> InspectRecorderAsync(HttpClient client, byte[] bytes)
{
    using var content = new ByteArrayContent(bytes);
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/recorder/imports/inspect")
    {
        Content = content
    };
    request.Headers.Add("X-File-Name", "fixture.rpablockly.zip");
    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException(
            $"Inspect Recorder falhou ({(int)response.StatusCode}): " +
            await response.Content.ReadAsStringAsync());
    }
    var value = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    return new RecorderInspection(
        value["stagingId"]!.GetValue<string>(),
        value["stagingToken"]!.GetValue<string>(),
        value["preview"]!.AsObject());
}

static async Task<JsonObject> PostRecorderDecisionAsync(
    HttpClient client,
    string stagingId,
    string stagingToken,
    string operation,
    JsonObject decision)
{
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/recorder/imports/{stagingId}/{operation}")
    {
        Content = JsonContent.Create(decision)
    };
    request.Headers.Add("X-Recorder-Staging-Token", stagingToken);
    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException(
            $"{operation} Recorder falhou ({(int)response.StatusCode}): " +
            await response.Content.ReadAsStringAsync());
    }
    return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
}

static JsonObject RecorderDecision(
    string revision,
    string mode,
    bool remap = false,
    string? subflowName = null) =>
    new()
    {
        ["expectedRevision"] = revision,
        ["mode"] = mode,
        ["subflowName"] = subflowName,
        ["remapConflicts"] = remap,
        ["inputMappings"] = new JsonObject(),
        ["secretMappings"] = new JsonObject(),
        ["attachmentMappings"] = new JsonObject(),
        ["resolvedIssueIds"] = new JsonArray()
    };

static byte[] BuildRecorderBundle(RpaPackageDocuments documents, string bundleId) =>
    ZipEntries(CreateRecorderEntries(documents, bundleId));

static Dictionary<string, byte[]> CreateRecorderEntries(
    RpaPackageDocuments documents,
    string bundleId,
    bool includeSecret = false)
{
    var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["package/flow.production.json"] = JsonBytes(documents.Flow),
        ["package/locators.production.json"] = JsonBytes(documents.Locators),
        ["package/rpa.policy.json"] = JsonBytes(documents.Policy),
        ["samples/inputs.sample.json"] = JsonBytes(new { input = new { } }),
        ["recording/events.json"] = JsonBytes(new { schemaVersion = 1, events = Array.Empty<object>() }),
        ["recording/issues.json"] = JsonBytes(new RecorderIssuesDocument()),
        ["recording/session.json"] = JsonBytes(new RecorderSessionDocument
        {
            SessionId = "session-editor-fixture",
            Name = "Importação Recorder em teste",
            State = RecorderSessionState.Completed,
            StartedAtUtc = DateTimeOffset.Parse("2026-08-17T18:00:00Z"),
            CompletedAtUtc = DateTimeOffset.Parse("2026-08-17T18:01:00Z"),
            Timezone = "America/Sao_Paulo",
            Locale = "pt-BR",
            Origins = ["https://fixture.test"],
            EventCount = 0
        })
    };
    if (includeSecret)
    {
        entries["secrets/index.json"] = JsonBytes(new RecorderSecretsIndexDocument
        {
            Items = ["secret.recorded.password"]
        });
        entries["secrets/password.json"] = JsonBytes(new RecorderEncryptedSecretEnvelope
        {
            Reference = "secret.recorded.password",
            KeyId = "fixture-recipient",
            Iv = Convert.ToBase64String(new byte[12]),
            Aad = Convert.ToBase64String("bundle:secret.recorded.password"u8),
            Ciphertext = Convert.ToBase64String(new byte[32]),
            WrappedKey = Convert.ToBase64String(new byte[256])
        });
    }
    var manifest = new RecorderBundleManifest
    {
        BundleId = bundleId,
        CreatedAtUtc = DateTimeOffset.Parse("2026-08-17T18:01:00Z"),
        DisplayName = "Bundle de teste do editor",
        HasSecrets = includeSecret,
        RecipientKeyId = includeSecret ? "fixture-recipient" : null,
        StepCount = documents.Flow.Actions.Count,
        Files = entries.Keys.OrderBy(path => path, StringComparer.Ordinal).ToList()
    };
    entries["manifest.json"] = JsonBytes(manifest);
    var integrity = new RecorderIntegrityDocument
    {
        Entries = entries.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new RecorderIntegrityEntry
            {
                Path = item.Key,
                Sha256 = Convert.ToHexString(SHA256.HashData(item.Value)).ToLowerInvariant(),
                Size = item.Value.LongLength
            }).ToList()
    };
    entries["integrity.json"] = JsonBytes(integrity);
    return entries;
}

static byte[] JsonBytes<T>(T value) =>
    new UTF8Encoding(false, true).GetBytes(
        JsonSerializer.Serialize(value, V2JsonSerializer.WriteOptions)
            .ReplaceLineEndings("\n") + "\n");

static byte[] ZipEntries(
    IReadOnlyDictionary<string, byte[]> entries,
    Action<ZipArchive>? append = null)
{
    using var output = new MemoryStream();
    using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var item in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var entry = archive.CreateEntry(item.Key, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open();
            stream.Write(item.Value);
        }
        append?.Invoke(archive);
    }
    return output.ToArray();
}

static Process StartEditor(string editorDll, string projectRoot, string editorUrl)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    startInfo.ArgumentList.Add(editorDll);
    startInfo.ArgumentList.Add("--project-root");
    startInfo.ArgumentList.Add(projectRoot);
    startInfo.ArgumentList.Add("--url");
    startInfo.ArgumentList.Add(editorUrl);
    startInfo.ArgumentList.Add("--no-open");
    return Process.Start(startInfo)
        ?? throw new InvalidOperationException("Não foi possível iniciar o editor de teste.");
}

static async Task WaitForEditorAsync(string editorUrl, Process process)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (DateTime.UtcNow < deadline)
    {
        if (process.HasExited)
        {
            throw new InvalidOperationException(
                "O editor encerrou antes de iniciar: " +
                await process.StandardError.ReadToEndAsync());
        }
        try
        {
            using var response = await client.GetAsync($"{editorUrl}/api/session");
            if (response.IsSuccessStatusCode) return;
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        await Task.Delay(100);
    }
    throw new TimeoutException("O editor não iniciou em 30 segundos.");
}

static int FindAvailablePort()
{
    var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException($"Falha: {description}.");
    Console.WriteLine($"OK: {description}.");
}

file sealed record BlockInspection(string Type, string[] Fields);
file sealed record RecorderInspection(
    string StagingId,
    string StagingToken,
    JsonObject Preview);
