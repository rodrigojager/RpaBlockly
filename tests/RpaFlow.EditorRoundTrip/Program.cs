using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Rpa.Worker.Configuration;
using Rpa.Worker.Data;
using Rpa.Worker.Domain;
using Rpa.Worker.Execution;
using RpaFlow.Contracts.Recorder;
using RpaFlow.Contracts.V2;
using RpaFlow.Packages;
using RpaFlow.Playwright;
using RpaFlow.Playwright.V2;
using RpaFlow.Runtime;

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
        await CheckRecorderImportApiAsync(editorUrl, testRoot, repositoryRoot);
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
    await File.WriteAllTextAsync(
        Path.Combine(testRoot, "appsettings.json"),
        """
        {
          "Runtime": {
            "ActionTimeoutSeconds": 30,
            "BusySelectors": [".loading"]
          },
          "Input": {
            "Url": "https://original.test/",
            "Aceite": false
          },
          "Blockly": {
            "Variables": {
              "preservada": "sim"
            }
          }
        }
        """.ReplaceLineEndings("\n") + "\n",
        utf8);
    await File.WriteAllTextAsync(
        Path.Combine(testRoot, "rpa.editor.json"),
        """
        {
          "displayName": "Editor V2 em teste",
          "projectFile": "EditorTest.csproj",
          "configurationFile": "appsettings.json",
          "rpaId": "editor-test",
          "packageStoreRoot": "package-store",
          "configurationFields": [
            {
              "path": "Input.Url",
              "label": "URL inicial",
              "source": "input.url",
              "type": "url"
            },
            {
              "path": "Input.Aceite",
              "label": "Aceite",
              "source": "input.aceite",
              "type": "checkbox"
            },
            {
              "path": "Runtime.ActionTimeoutSeconds",
              "label": "Timeout",
              "type": "number"
            },
            {
              "path": "Runtime.BusySelectors",
              "label": "Loaders",
              "type": "stringList",
              "nullable": true
            }
          ]
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

    Check(await page.GetAttributeAsync("#policy-json", "readonly") is not null,
        "JSON da policy é somente visualização e não exige edição manual");
    await page.SelectOptionAsync("#policy-mode", "fallback");
    await page.ClickAsync("#save-policy-draft");
    var appliedPolicy = JsonNode.Parse(await page.EvaluateAsync<string>(
        "() => JSON.stringify(window.RpaFlowEditorTesting.packagePolicy())"))!.AsObject();
    Check(appliedPolicy["locatorResilience"]?["mode"]?.GetValue<string>() == "fallback",
        "controles tipados da policy aplicam fallback ao rascunho");

    await page.ClickAsync("#open-configuration");
    await page.Locator("#configuration-dialog").WaitForAsync(
        new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    Check(await page.Locator("#configuration-dialog").GetAttributeAsync("open") is not null,
        "configuração local abre em formulário tipado");
    await page.FillAsync(
        "[data-configuration-path='Input.Url']",
        "https://alterada.test/");
    await page.CheckAsync("[data-configuration-path='Input.Aceite']");
    await page.FillAsync(
        "[data-configuration-path='Runtime.ActionTimeoutSeconds']",
        "45");
    await page.FillAsync(
        "[data-configuration-path='Runtime.BusySelectors']",
        ".loading\n[aria-busy='true']");
    await page.ClickAsync("#save-configuration");
    await page.Locator("#configuration-dialog").WaitForAsync(
        new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    var savedConfiguration = JsonNode.Parse(await File.ReadAllTextAsync(
        Path.Combine(testRoot, "appsettings.json")))!.AsObject();
    Check(savedConfiguration["Input"]?["Url"]?.GetValue<string>() ==
          "https://alterada.test/" &&
          savedConfiguration["Input"]?["Aceite"]?.GetValue<bool>() == true &&
          savedConfiguration["Runtime"]?["ActionTimeoutSeconds"]?.GetValue<int>() == 45 &&
          savedConfiguration["Runtime"]?["BusySelectors"]?.AsArray().Count == 2,
        "formulário salva URL, booleano, número e lista sem editar JSON");
    Check(savedConfiguration["Blockly"]?["Variables"]?["preservada"]
              ?.GetValue<string>() == "sim",
        "campos ocultos da configuração são preservados");

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

static async Task CheckRecorderImportApiAsync(
    string editorUrl,
    string testRoot,
    string repositoryRoot)
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
    await CheckProductionRecorderPipelineAsync(
        client,
        store,
        repositoryRoot,
        testRoot);

    using var delete = new HttpRequestMessage(
        HttpMethod.Delete,
        $"/api/recorder/imports/{subflowInspection.StagingId}");
    delete.Headers.Add("X-Recorder-Staging-Token", subflowInspection.StagingToken);
    using var deleted = await client.SendAsync(delete);
    Check(deleted.StatusCode == HttpStatusCode.NoContent,
        "cancelamento remove staging de forma idempotente");
}

static async Task CheckProductionRecorderPipelineAsync(
    HttpClient client,
    FileRpaPackageStore store,
    string repositoryRoot,
    string testRoot)
{
    var fixtureRoot = Path.Combine(repositoryRoot, "tests", "fixtures", "recorder-site");
    await using var fixture = RecorderFixtureServer.Start(fixtureRoot);
    var extensionRoot = Path.Combine(
        repositoryRoot,
        "src",
        "RpaFlow.Recorder.Extension");
    var contentScript = Path.Combine(extensionRoot, "build", "content", "content-script.js");
    Check(File.Exists(contentScript),
        "o E2E usa o content script compilado da extensão, não uma cópia de teste");

    var uploadPath = Path.Combine(testRoot, "recorder-fixture-upload.txt");
    await File.WriteAllTextAsync(
        uploadPath,
        "arquivo sanitizado da fixture\n",
        new UTF8Encoding(false, true));
    await RecorderExtensionLifecycle.VerifyLoadedAsync(
        Path.Combine(extensionRoot, "build"),
        testRoot);
    Check(true,
        "o Chromium carrega manifesto, service worker e side panel MV3 empacotados");
    var messages = await CaptureRecorderMessagesAsync(
        fixture.BaseUrl,
        contentScript,
        uploadPath);
    Check(messages.Count >= 9,
        "a extensão captura formulário, SPA, upload, scope, shadow DOM aberto, DOM dinâmico e iframe");
    Check(messages.Any(item =>
            item?["event"]?["type"]?.GetValue<string>() == "select"),
        "a extensão registra a seleção nativa como selectOption");
    Check(messages.Any(item =>
            item?["event"]?["target"]?["scope"]?["selector"]?
                .GetValue<string>() == "[data-testid=\"scope-primary\"]"),
        "a extensão cria scope estável quando o alvo não é globalmente único");
    Check(messages.Any(item =>
            item?["event"]?["target"]?["attributes"]?["data-testid"]?
                .GetValue<string>() == "shadow-action" &&
            item?["event"]?["target"]?["closedShadowRoot"]?
                .GetValue<bool>() == false),
        "a extensão captura alvo executável dentro de shadow root aberto");
    var serializedMessages = messages.ToJsonString(new JsonSerializerOptions
    {
        WriteIndented = true
    });
    Check(!serializedMessages.Contains("fakepath", StringComparison.OrdinalIgnoreCase) &&
          !serializedMessages.Contains("SegredoNuncaSerializado42!", StringComparison.Ordinal),
        "a captura não serializa caminho do upload nem senha em texto claro");

    var messagePath = Path.Combine(testRoot, "recorder-captured-messages.json");
    var bundlePath = Path.Combine(testRoot, "recorder-production-bundle.rpablockly.zip");
    await File.WriteAllTextAsync(
        messagePath,
        serializedMessages.ReplaceLineEndings("\n") + "\n",
        new UTF8Encoding(false, true));
    RunNode(
        extensionRoot,
        "scripts/export-captured.mjs",
        messagePath,
        bundlePath);
    var bundle = await File.ReadAllBytesAsync(bundlePath);
    var inspection = await InspectRecorderAsync(client, bundle);
    Check(inspection.Preview["bundleId"]?.GetValue<string>() ==
          "bundle-recorder-e2e-fixture",
        "o editor real inspeciona o ZIP emitido pelo código de produção do Recorder");

    var current = await store.LoadAsync("editor-test", null, CancellationToken.None);
    var decision = RecorderDecision(current.Revision.Value, "replace");
    var inputMappings = new JsonObject();
    foreach (var path in Strings(inspection.Preview, "recordedInputPaths"))
    {
        inputMappings[path] = "input.fixture." + path.Split('.').Last();
    }
    var attachmentMappings = new JsonObject();
    foreach (var path in Strings(inspection.Preview, "attachmentReferences"))
    {
        attachmentMappings[path] = "attachments.fixture." + path.Split('.').Last();
    }
    decision["inputMappings"] = inputMappings;
    decision["attachmentMappings"] = attachmentMappings;
    var validation = await PostRecorderDecisionAsync(
        client,
        inspection.StagingId,
        inspection.StagingToken,
        "validate",
        decision);
    Check(validation["canApply"]?.GetValue<bool>() == true,
        "review e mapeamentos do bundle de produção são válidos antes do apply");
    var applied = await PostRecorderDecisionAsync(
        client,
        inspection.StagingId,
        inspection.StagingToken,
        "apply",
        decision);
    Check(applied["flow"]?["actions"]?.AsArray().Count > 5,
        "o editor aplica o roteiro gravado sem edição manual de JSON");

    var snapshot = await store.LoadAsync("editor-test", null, CancellationToken.None);
    var (input, attachments) = ReadExecutionData(
        bundle,
        inputMappings,
        attachmentMappings,
        uploadPath);
    var options = new PlaywrightRuntimeOptions(
        Headless: true,
        Browser: Environment.GetEnvironmentVariable("RPABLOCKLY_CHECKS_BROWSER") ?? "chromium",
        ActionTimeoutSeconds: 15,
        UploadTimeoutSeconds: 15,
        OutputDirectory: "recorder-e2e-artifacts",
        ConfigurationDirectory: testRoot,
        ReadinessQuietPeriodMs: 50,
        FormStabilityMs: 50);
    var workerRepository = await ExecuteRecorderPackageThroughWorkerAsync(
        repositoryRoot,
        testRoot,
        input,
        attachments,
        options.Browser);
    Check(workerRepository.Status == "Succeeded" &&
          workerRepository.ExecutedActions == snapshot.Flow.Actions.Count &&
          workerRepository.Failure is null,
        "Recorder → ZIP → Editor → File Store → Worker → Runtime conclui em strict");
    Check(!workerRepository.Events.Any(item => item.Kind == "locatorFallbackSelected"),
        "strict usa somente o candidato primário gravado");

    fixture.ChangedDom = true;
    var fallbackDocuments = snapshot.CopyDocuments();
    fallbackDocuments.Policy.LocatorResilience.Mode = LocatorResilienceMode.Fallback;
    _ = await store.PublishAsync(
        "editor-test",
        fallbackDocuments,
        snapshot.Revision,
        CancellationToken.None);
    var fallbackSnapshot = await store.LoadAsync("editor-test", null, CancellationToken.None);
    var fallbackObserver = new RecorderE2EObserver();
    var fallbackResult = await new PlaywrightV2FlowExecutor(
            fallbackSnapshot,
            options,
            fallbackObserver)
        .ExecuteAsync(
            new FlowExecutionRequest("recorder-e2e-fallback", input, [], attachments),
            CancellationToken.None);
    Check(fallbackResult.ExecutedActions == fallbackSnapshot.Flow.Actions.Count &&
          fallbackObserver.Events.Any(item => item.Kind == "locatorFallbackSelected"),
        "fallback conclui após a alteração controlada do localizador primário no DOM");

    var adaptiveDocuments = fallbackSnapshot.CopyDocuments();
    adaptiveDocuments.Policy.LocatorResilience.Mode = LocatorResilienceMode.Adaptive;
    adaptiveDocuments.Policy.LocatorResilience.LearningWriteBack =
        LearningWriteBackMode.Memory;
    adaptiveDocuments.Policy.LocatorResilience.Promotion =
        LocatorPromotionMode.AfterSuccessfulExecution;
    _ = await store.PublishAsync(
        "editor-test",
        adaptiveDocuments,
        fallbackSnapshot.Revision,
        CancellationToken.None);
    var safeBoundaryActionId = adaptiveDocuments.Flow.Actions[^1].Id;
    var validatedRepository = await ExecuteRecorderPackageThroughWorkerAsync(
        repositoryRoot,
        testRoot,
        input,
        attachments,
        options.Browser,
        WorkerExecutionMode.SafeValidation,
        safeBoundaryActionId);
    Check(validatedRepository.Status == "Validated" &&
          validatedRepository.Events.Any(item =>
              item.Kind == "locatorPromotionDiscarded") &&
          !validatedRepository.Events.Any(item =>
              item.Kind == "locatorPromotionCompleted"),
        "worker descarta aprendizado adaptativo quando o resultado final é Validated");
}

static async Task<RecorderWorkerRepository> ExecuteRecorderPackageThroughWorkerAsync(
    string repositoryRoot,
    string testRoot,
    JsonObject input,
    JsonObject attachments,
    string browser,
    WorkerExecutionMode executionMode = WorkerExecutionMode.Production,
    string? safeValidationBoundaryActionId = null)
{
    var options = new RpaWorkerOptions
    {
        Enabled = true,
        ExecutionMode = executionMode,
        WorkerId = "recorder-e2e-worker",
        HeartbeatSeconds = 3_600,
        CaseTimeoutMinutes = 2,
        Definitions = new Dictionary<string, RpaDefinitionOptions>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["recorder-e2e"] = new RpaDefinitionOptions
            {
                Enabled = true,
                ClaimEnabled = true,
                SafeValidationBoundaryActionId = safeValidationBoundaryActionId,
                Package = new RpaPackageReferenceOptions
                {
                    RpaId = "editor-test",
                    Provider = "File",
                    OriginName = "source",
                    Location = Path.Combine(testRoot, "package-store")
                },
                Runtime = new RpaRuntimeOptions
                {
                    Headless = true,
                    Browser = browser,
                    ActionTimeoutSeconds = 15,
                    UploadTimeoutSeconds = 15,
                    ReadinessQuietPeriodMs = 50,
                    FormStabilityMs = 50
                }
            }
        }
    };
    var paths = new WorkerPaths(
        testRoot,
        repositoryRoot,
        Path.Combine(testRoot, "worker-artifacts"),
        Path.Combine(testRoot, "worker-sessions"));
    Directory.CreateDirectory(paths.ArtifactRoot);
    Directory.CreateDirectory(paths.SessionStateRoot);
    var environment = new WorkerEnvironment(string.Empty, paths);
    var registry = RpaPackageRegistryFactory.Create(options, environment);
    var repository = new RecorderWorkerRepository();
    var processor = new WorkItemProcessor(
        options,
        environment,
        registry,
        repository,
        new RecorderOneTimeCodeProvider(),
        NullLogger<WorkItemProcessor>.Instance);
    var workItem = new RpaWorkItem(
        Guid.NewGuid(),
        "recorder-e2e",
        "recorder-e2e-batch",
        null,
        1,
        1,
        input.ToJsonString(),
        "{}",
        attachments.ToJsonString());
    await processor.ProcessAsync(workItem, CancellationToken.None);
    return repository;
}

static async Task<JsonArray> CaptureRecorderMessagesAsync(
    string baseUrl,
    string contentScript,
    string uploadPath)
{
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(
        new BrowserTypeLaunchOptions { Headless = true });
    var context = await browser.NewContextAsync();
    await context.AddInitScriptAsync(
        """
        (() => {
          globalThis.__recorderMessages = [];
          globalThis.chrome ??= {};
          globalThis.chrome.runtime = {
            sendMessage: async message => {
              const owner = globalThis.top ?? globalThis;
              owner.__recorderMessages ??= [];
              owner.__recorderMessages.push(JSON.parse(JSON.stringify(message)));
              return { ok: true };
            },
            onMessage: { addListener: listener => {
              globalThis.__recorderOnMessage = listener;
            } }
          };
        })();
        """);
    var page = await context.NewPageAsync();
    await page.GotoAsync(
        baseUrl + "/index.html",
        new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await page.AddScriptTagAsync(new PageAddScriptTagOptions
    {
        Path = contentScript,
        Type = "module"
    });
    var frame = page.Frames.Single(item => item != page.MainFrame);
    await frame.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    await frame.AddScriptTagAsync(new FrameAddScriptTagOptions
    {
        Path = contentScript,
        Type = "module"
    });

    await page.EvaluateAsync(
        "() => { document.querySelector('#senha').value = 'SegredoNuncaSerializado42!'; }");
    await page.FillAsync("#nome", "Maria da Silva");
    await page.ClickAsync("#estado");
    await page.Keyboard.PressAsync("ArrowDown");
    await page.Keyboard.PressAsync("Enter");
    await page.Keyboard.PressAsync("Tab");
    await page.CheckAsync("#aceite");
    await page.SetInputFilesAsync("#arquivo", uploadPath);
    await page.ClickAsync("#spa-next");
    await page.ClickAsync("#dynamic-action");
    await page.ClickAsync("[data-testid='scope-primary'] button");
    await page.Locator("#shadow-host").Locator("[data-testid='shadow-action']").ClickAsync();
    await frame.ClickAsync("#frame-action");
    await page.WaitForFunctionAsync(
        "() => (globalThis.__recorderMessages?.length ?? 0) >= 9");
    var json = await page.EvaluateAsync<string>(
        "() => JSON.stringify(globalThis.__recorderMessages ?? [])");
    return JsonNode.Parse(json)?.AsArray()
        ?? throw new InvalidOperationException("Mensagens da extensão estão vazias.");
}

static IReadOnlyList<string> Strings(JsonObject owner, string property) =>
    owner[property]?.AsArray()
        .Select(item => item?.GetValue<string>())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Select(item => item!)
        .ToArray() ?? [];

static (JsonObject Input, JsonObject Attachments) ReadExecutionData(
    byte[] bundle,
    JsonObject inputMappings,
    JsonObject attachmentMappings,
    string uploadPath)
{
    using var stream = new MemoryStream(bundle);
    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
    var sampleEntry = archive.GetEntry("samples/inputs.sample.json")
        ?? throw new InvalidOperationException("Bundle sem samples de entrada.");
    using var reader = new StreamReader(sampleEntry.Open(), Encoding.UTF8);
    var samples = JsonNode.Parse(reader.ReadToEnd())!.AsObject()["input"]!.AsObject();
    var fixtureInput = new JsonObject();
    foreach (var mapping in inputMappings)
    {
        var sourceKey = mapping.Key.Split('.').Last();
        var targetKey = mapping.Value!.GetValue<string>().Split('.').Last();
        fixtureInput[targetKey] = samples[sourceKey]?.DeepClone();
    }
    var fixtureAttachments = new JsonObject();
    foreach (var mapping in attachmentMappings)
    {
        var targetKey = mapping.Value!.GetValue<string>().Split('.').Last();
        fixtureAttachments[targetKey] = uploadPath;
    }
    return (
        new JsonObject { ["fixture"] = fixtureInput },
        new JsonObject { ["fixture"] = fixtureAttachments });
}

static void RunNode(string workingDirectory, params string[] arguments)
{
    var startInfo = new ProcessStartInfo("node")
    {
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Não foi possível iniciar o exportador Recorder.");
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    var output = outputTask.GetAwaiter().GetResult();
    var error = errorTask.GetAwaiter().GetResult();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"Exportador Recorder falhou ({process.ExitCode}): {error}\n{output}");
    }
    Console.WriteLine(output.Trim());
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

file sealed class RecorderE2EObserver : IFlowExecutionObserver
{
    public List<FlowExecutionEvent> Events { get; } = [];

    public ValueTask ObserveAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add(executionEvent);
        return ValueTask.CompletedTask;
    }
}

file sealed class RecorderWorkerRepository : IWorkItemExecutionRepository
{
    public string? Status { get; private set; }

    public int ExecutedActions { get; private set; }

    public Exception? Failure { get; private set; }

    public List<FlowExecutionEvent> Events { get; } = [];

    public Task StartExecutionAsync(
        string executionId,
        RpaWorkItem workItem,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task SetExecutionPackageAsync(
        string executionId,
        string originName,
        RpaPackageSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.RpaId != "editor-test" || originName != "source")
        {
            throw new InvalidOperationException("O worker não fixou o pacote importado esperado.");
        }
        return Task.CompletedTask;
    }

    public Task RenewLeaseAsync(Guid workItemId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task CompleteAsync(
        string executionId,
        RpaWorkItem workItem,
        string status,
        string outputJson,
        int executedActions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Status = status;
        ExecutedActions = executedActions;
        return Task.CompletedTask;
    }

    public Task FailAsync(
        string executionId,
        RpaWorkItem workItem,
        Exception exception,
        bool allowRetry,
        CancellationToken cancellationToken)
    {
        Failure = exception;
        Status = allowRetry && workItem.AttemptCount < workItem.MaxAttempts
            ? "Retry"
            : "Failed";
        return Task.CompletedTask;
    }

    public Task SaveOutputsAsync(
        string executionId,
        RpaWorkItem workItem,
        IReadOnlyList<MaterializedOutput> outputs,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SaveArtifactsAsync(
        string executionId,
        RpaWorkItem workItem,
        IReadOnlyList<MaterializedArtifact> artifacts,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AppendEventAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add(executionEvent);
        return Task.CompletedTask;
    }
}

file sealed class RecorderOneTimeCodeProvider : IOneTimeCodeProvider
{
    public Task<OneTimeCodeResult> WaitForCodeAsync(
        OneTimeCodeRequest request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("A fixture Recorder não solicita código de uso único.");
}
