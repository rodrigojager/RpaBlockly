using System.Diagnostics;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using RpaFlow.Contracts;
using RpaFlow.Editor.Validation;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Uso: RpaFlow.EditorRoundTrip <RpaFlow.Editor.dll> <raiz-do-workspace>.");
    return 2;
}

var editorDll = Path.GetFullPath(args[0]);
var repositoryRoot = Path.GetFullPath(args[1]);
if (!File.Exists(editorDll) ||
    !File.Exists(Path.Combine(repositoryRoot, "RpaBlockly.slnx")))
{
    Console.Error.WriteLine("Editor compilado ou raiz do workspace não encontrados.");
    return 2;
}

var port = FindAvailablePort();
var editorUrl = $"http://127.0.0.1:{port}";
using var editor = StartEditor(editorDll, repositoryRoot, editorUrl);
try
{
    await WaitForEditorAsync(editorUrl, editor);
    await RunRoundTripChecksAsync(editorUrl, repositoryRoot);
    return 0;
}
finally
{
    if (!editor.HasExited)
    {
        editor.Kill(entireProcessTree: true);
        await editor.WaitForExitAsync();
    }
}

static async Task RunRoundTripChecksAsync(string editorUrl, string repositoryRoot)
{
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(
        new BrowserTypeLaunchOptions { Headless = true });
    var page = await browser.NewPageAsync();
    await page.GotoAsync(
        $"{editorUrl}/?roundtrip-test=1",
        new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await page.WaitForFunctionAsync(
        "() => Boolean(window.RpaFlowEditorTesting)");

    await CheckSharedToolboxAsync(page);

    foreach (var project in new[] { Path.Combine("examples", "RpaExemplo") })
    {
        var flowPath = Path.Combine(repositoryRoot, project, "flow.production.json");
        var originalJson = await File.ReadAllTextAsync(flowPath);
        var exportedJson = await page.EvaluateAsync<string>(
            "flowJson => JSON.stringify(window.RpaFlowEditorTesting.roundTrip(JSON.parse(flowJson)))",
            originalJson);

        var original = DeserializeAndNormalize(originalJson, project);
        var exported = DeserializeAndNormalize(exportedJson, project);
        var originalSnapshot = SerializeCanonical(original);
        var exportedSnapshot = SerializeCanonical(exported);
        if (!string.Equals(originalSnapshot, exportedSnapshot, StringComparison.Ordinal))
        {
            var failureDirectory = Path.Combine(repositoryRoot, "tmp", "roundtrip-failures");
            Directory.CreateDirectory(failureDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(failureDirectory, $"{project}.original.json"),
                originalSnapshot);
            await File.WriteAllTextAsync(
                Path.Combine(failureDirectory, $"{project}.exported.json"),
                exportedSnapshot);
            throw new InvalidOperationException(
                $"O round-trip Blockly alterou a semântica de {project}. " +
                $"Veja os snapshots em {failureDirectory}.");
        }

        Console.WriteLine($"OK: round-trip Blockly preservou {project}.");
    }

    await CheckGeneralizedPropertiesRoundTripAsync(page);
    await CheckOneTimeCodeActionsRoundTripAsync(page, repositoryRoot);
    await CheckCompleteAuthenticationAttemptRoundTripAsync(page, repositoryRoot);
    await CheckTypeAcrossInputsRoundTripAsync(page, repositoryRoot);
    await CheckSafeFinalConfirmationRoundTripAsync(page, repositoryRoot);
}

static async Task CheckCompleteAuthenticationAttemptRoundTripAsync(
    IPage page,
    string repositoryRoot)
{
    var fixturePath = Path.Combine(
        repositoryRoot,
        "tests",
        "RpaFlow.EditorRoundTrip",
        "Fixtures",
        "complete-authentication-attempt.valid.json");
    var fixture = await File.ReadAllTextAsync(fixturePath);

    using (var fixtureDocument = JsonDocument.Parse(fixture))
    {
        FlowDocumentValidator.Validate(fixtureDocument.RootElement);
    }

    var exportedJson = await page.EvaluateAsync<string>(
        "flowJson => JSON.stringify(window.RpaFlowEditorTesting.roundTrip(JSON.parse(flowJson)))",
        fixture);
    var original = SerializeCanonical(
        DeserializeAndNormalize(fixture, "fixture-conclusao-autenticacao"));
    var exported = SerializeCanonical(
        DeserializeAndNormalize(exportedJson, "fixture-conclusao-autenticacao"));
    if (!string.Equals(original, exported, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "O round-trip Blockly alterou a conclusão da tentativa de autenticação.");
    }

    Console.WriteLine(
        "OK: round-trip Blockly preservou a conclusão da tentativa de autenticação.");
}

static async Task CheckSafeFinalConfirmationRoundTripAsync(
    IPage page,
    string repositoryRoot)
{
    var fixturePath = Path.Combine(
        repositoryRoot,
        "tests",
        "RpaFlow.EditorRoundTrip",
        "Fixtures",
        "safe-final-confirmation.valid.json");
    var fixture = await File.ReadAllTextAsync(fixturePath);

    using (var fixtureDocument = JsonDocument.Parse(fixture))
    {
        FlowDocumentValidator.Validate(fixtureDocument.RootElement);
    }

    var exportedJson = await page.EvaluateAsync<string>(
        "flowJson => JSON.stringify(window.RpaFlowEditorTesting.roundTrip(JSON.parse(flowJson)))",
        fixture);
    var original = SerializeCanonical(
        DeserializeAndNormalize(fixture, "fixture-confirmacao-final"));
    var exported = SerializeCanonical(
        DeserializeAndNormalize(exportedJson, "fixture-confirmacao-final"));
    if (!string.Equals(original, exported, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "O round-trip Blockly alterou os critérios da confirmação final.");
    }

    var legacyNode = JsonNode.Parse(fixture)?.AsObject()
        ?? throw new InvalidOperationException(
            "Não foi possível preparar a confirmação final sem feedback.");
    var legacyAction = legacyNode["actions"]?.AsArray()[0]?.AsObject()
        ?? throw new InvalidOperationException(
            "A fixture da confirmação final não possui a ação esperada.");
    foreach (var propertyName in new[]
             {
                 "successSelector",
                 "successText",
                 "protocolSelector",
                 "protocolPattern",
                 "completionTarget",
                 "confirmationMessageTarget",
                 "protocolTarget",
                 "timeoutMs"
             })
    {
        legacyAction.Remove(propertyName);
    }

    var legacyFixture = legacyNode.ToJsonString();
    using (var legacyDocument = JsonDocument.Parse(legacyFixture))
    {
        FlowDocumentValidator.Validate(legacyDocument.RootElement);
    }

    var legacyExportedJson = await page.EvaluateAsync<string>(
        "flowJson => JSON.stringify(window.RpaFlowEditorTesting.roundTrip(JSON.parse(flowJson)))",
        legacyFixture);
    var legacyOriginal = SerializeCanonical(
        DeserializeAndNormalize(legacyFixture, "fixture-confirmacao-final-sem-feedback"));
    var legacyExported = SerializeCanonical(
        DeserializeAndNormalize(
            legacyExportedJson,
            "fixture-confirmacao-final-sem-feedback"));
    if (!string.Equals(legacyOriginal, legacyExported, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "O Blockly habilitou a comprovação de conclusão que estava desmarcada.");
    }

    var invalidFixtures = new[]
    {
        MutateSafeFinalConfirmationFixture(
            fixture,
            action => action.Remove("successText")),
        MutateSafeFinalConfirmationFixture(
            fixture,
            action => action["protocolPattern"] = @"#(\d+)"),
        MutateSafeFinalConfirmationFixture(
            fixture,
            action => action["protocolTarget"] = "runtime.business.completed")
    };

    foreach (var invalidFixture in invalidFixtures)
    {
        using var invalidDocument = JsonDocument.Parse(invalidFixture);
        try
        {
            FlowDocumentValidator.Validate(invalidDocument.RootElement);
            throw new InvalidOperationException(
                "O microservidor aceitou uma confirmação final inválida.");
        }
        catch (InvalidOperationException exception) when (
            !exception.Message.StartsWith(
                "O microservidor aceitou",
                StringComparison.Ordinal))
        {
        }

        var editorError = await page.EvaluateAsync<string?>(
            """
            flowJson => {
              try {
                window.RpaFlowEditorTesting.roundTrip(JSON.parse(flowJson));
                return null;
              } catch (error) {
                return String(error?.message || error);
              }
            }
            """,
            invalidFixture);
        if (string.IsNullOrWhiteSpace(editorError))
        {
            throw new InvalidOperationException(
                "O editor aceitou uma confirmação final inválida.");
        }
    }

    Console.WriteLine(
        "OK: round-trip e validações preservaram a confirmação final configurável.");
}

static string MutateSafeFinalConfirmationFixture(
    string fixture,
    Action<JsonObject> mutate)
{
    var root = JsonNode.Parse(fixture)?.AsObject()
        ?? throw new InvalidOperationException(
            "Não foi possível preparar a confirmação final inválida.");
    var action = root["actions"]?.AsArray()[0]?.AsObject()
        ?? throw new InvalidOperationException(
            "A fixture da confirmação final não possui a ação esperada.");

    mutate(action);
    return root.ToJsonString();
}

static async Task CheckTypeAcrossInputsRoundTripAsync(
    IPage page,
    string repositoryRoot)
{
    var fixturePath = Path.Combine(
        repositoryRoot,
        "tests",
        "RpaFlow.EditorRoundTrip",
        "Fixtures",
        "type-across-inputs.valid.json");
    var fixture = await File.ReadAllTextAsync(fixturePath);

    using (var fixtureDocument = JsonDocument.Parse(fixture))
    {
        FlowDocumentValidator.Validate(fixtureDocument.RootElement);
    }

    var exportedJson = await page.EvaluateAsync<string>(
        "flowJson => JSON.stringify(window.RpaFlowEditorTesting.roundTrip(JSON.parse(flowJson)))",
        fixture);
    var original = SerializeCanonical(
        DeserializeAndNormalize(fixture, "fixture-digitacao-segmentada"));
    var exported = SerializeCanonical(
        DeserializeAndNormalize(exportedJson, "fixture-digitacao-segmentada"));
    if (!string.Equals(original, exported, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "O round-trip Blockly alterou a ação de digitação segmentada.");
    }

    var invalidFixtures = new[]
    {
        fixture.Replace(
            "\"valueSource\": \"runtime.authentication.otp\"",
            "\"value\": \"123456\",\n      \"valueSource\": \"runtime.authentication.otp\"",
            StringComparison.Ordinal),
        fixture.Replace(
            "\"type\": \"typeAcrossInputs\"",
            "\"type\": \"typeAcrossInputs\",\n      \"matchMode\": \"single\"",
            StringComparison.Ordinal),
        fixture.Replace("\"delayMs\": 75", "\"delayMs\": 1001", StringComparison.Ordinal),
        fixture.Replace("\"clearFirst\": true", "\"clearFirst\": \"true\"", StringComparison.Ordinal),
        fixture.Replace(
            "\"selector\": \"input[data-cy='codigo-segmento']\"",
            "\"selector\": \"\"",
            StringComparison.Ordinal)
    };

    foreach (var invalidFixture in invalidFixtures)
    {
        using var invalidDocument = JsonDocument.Parse(invalidFixture);
        try
        {
            FlowDocumentValidator.Validate(invalidDocument.RootElement);
            throw new InvalidOperationException(
                "O microservidor aceitou uma digitação segmentada inválida.");
        }
        catch (InvalidOperationException exception) when (
            !exception.Message.StartsWith(
                "O microservidor aceitou",
                StringComparison.Ordinal))
        {
        }

        var editorError = await page.EvaluateAsync<string?>(
            """
            flowJson => {
              try {
                window.RpaFlowEditorTesting.roundTrip(JSON.parse(flowJson));
                return null;
              } catch (error) {
                return String(error?.message || error);
              }
            }
            """,
            invalidFixture);
        if (string.IsNullOrWhiteSpace(editorError))
        {
            throw new InvalidOperationException(
                "O editor aceitou uma digitação segmentada inválida.");
        }
    }

    Console.WriteLine(
        "OK: round-trip e validações preservaram a digitação segmentada.");
}

static async Task CheckOneTimeCodeActionsRoundTripAsync(
    IPage page,
    string repositoryRoot)
{
    var fixturePath = Path.Combine(
        repositoryRoot,
        "tests",
        "RpaFlow.EditorRoundTrip",
        "Fixtures",
        "one-time-code-actions.valid.json");
    var fixture = await File.ReadAllTextAsync(fixturePath);

    using (var fixtureDocument = JsonDocument.Parse(fixture))
    {
        FlowDocumentValidator.Validate(fixtureDocument.RootElement);
    }

    var exportedJson = await page.EvaluateAsync<string>(
        "flowJson => JSON.stringify(window.RpaFlowEditorTesting.roundTrip(JSON.parse(flowJson)))",
        fixture);
    var original = SerializeCanonical(
        DeserializeAndNormalize(fixture, "fixture-codigo-autenticacao"));
    var exported = SerializeCanonical(
        DeserializeAndNormalize(exportedJson, "fixture-codigo-autenticacao"));
    if (!string.Equals(original, exported, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "O round-trip Blockly alterou as ações de autenticação por código.");
    }

    var invalidFixtures = new[]
    {
        fixture.Replace(
            "\"email-otp\"",
            "\"alias inválido\"",
            StringComparison.Ordinal),
        fixture.Replace(
            "\"notBeforeSource\": \"runtime.authentication.otpRequestedAt\"",
            "\"notBeforeSource\": \"origem-inválida\"",
            StringComparison.Ordinal),
        fixture.Replace(
            "\"runtime.authentication.otp\"",
            "\"input.authentication.otp\"",
            StringComparison.Ordinal),
        fixture.Replace("120000", "999", StringComparison.Ordinal),
        fixture.Replace("5000", "499", StringComparison.Ordinal),
        fixture.Replace("120000", "1000", StringComparison.Ordinal)
    };

    foreach (var invalidFixture in invalidFixtures)
    {
        using var invalidDocument = JsonDocument.Parse(invalidFixture);
        try
        {
            FlowDocumentValidator.Validate(invalidDocument.RootElement);
            throw new InvalidOperationException(
                "O microservidor aceitou uma configuração inválida de autenticação por código.");
        }
        catch (InvalidOperationException exception) when (
            !exception.Message.StartsWith(
                "O microservidor aceitou",
                StringComparison.Ordinal))
        {
        }
    }

    foreach (var invalidFixture in invalidFixtures.Take(3).Append(invalidFixtures[^1]))
    {
        var error = await page.EvaluateAsync<string?>(
            """
            flowJson => {
              try {
                window.RpaFlowEditorTesting.roundTrip(JSON.parse(flowJson));
                return null;
              } catch (error) {
                return String(error?.message || error);
              }
            }
            """,
            invalidFixture);
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException(
                "O editor aceitou uma configuração inválida de autenticação por código.");
        }
    }

    Console.WriteLine(
        "OK: round-trip e validações preservaram as ações de autenticação por código.");
}

static async Task CheckGeneralizedPropertiesRoundTripAsync(IPage page)
{
    const string fixture =
        """
        {
          "schemaVersion": 1,
          "name": "Round-trip das propriedades generalizadas",
          "actions": [
            {
              "id": "aguardar-unico",
              "type": "wait",
              "name": "Aguardar elemento único",
              "selector": ".resultado",
              "state": "visible",
              "matchMode": "single"
            },
            {
              "id": "selecionar-texto",
              "type": "select2",
              "name": "Selecionar por texto",
              "selector": "#tipo",
              "triggerSelector": ".select2-selection",
              "optionSelector": ".select2-results__option",
              "valueSource": "input.tipo",
              "comparison": "caseInsensitive"
            },
            {
              "id": "preencher-mascara",
              "type": "fillMaskedCurrency",
              "name": "Preencher máscara",
              "selector": "#valor",
              "valueSource": "input.valor",
              "decimalPlaces": 3,
              "delayMs": 15,
              "commitKey": "Enter"
            },
            {
              "id": "selecionar-nativo",
              "type": "selectOption",
              "name": "Selecionar opção nativa",
              "selector": "#tipo-nativo",
              "optionMode": "label",
              "value": "Serviço"
            },
            {
              "id": "marcar-aceite",
              "type": "setChecked",
              "name": "Marcar aceite",
              "selector": "#aceite",
              "value": true
            },
            {
              "id": "pressionar-enter",
              "type": "pressKey",
              "name": "Pressionar Enter",
              "selector": "#pesquisa",
              "value": "Enter"
            },
            {
              "id": "ler-linhas",
              "type": "readElements",
              "name": "Ler linhas",
              "selector": ".linha",
              "property": "text",
              "maxItems": 50,
              "target": "runtime.linhas"
            },
            {
              "id": "assumir-relatorio",
              "type": "switchPage",
              "name": "Assumir relatório",
              "property": "url",
              "comparison": "contains",
              "value": "/relatorio",
              "readySelector": "#resultado"
            },
            {
              "id": "fechar-relatorio",
              "type": "closePage",
              "name": "Fechar relatório",
              "readySelector": "#origem"
            },
            {
              "id": "verificar-unico",
              "type": "if",
              "name": "Verificar elemento único",
              "condition": {
                "type": "element",
                "selector": ".confirmacao",
                "state": "attached",
                "matchMode": "single"
              },
              "actions": [
                {
                  "id": "guardar-confirmacao",
                  "type": "setVariable",
                  "name": "Guardar confirmação",
                  "value": true,
                  "target": "runtime.confirmado"
                }
              ]
            },
            {
              "id": "verificar-lista-tipada",
              "type": "if",
              "name": "Verificar lista tipada",
              "condition": {
                "type": "value",
                "leftValue": [1, 2, 3],
                "operator": "contains",
                "rightValue": 2
              },
              "actions": [
                {
                  "id": "guardar-condicao-verdadeira",
                  "type": "setVariable",
                  "name": "Guardar condição verdadeira",
                  "value": true,
                  "target": "runtime.condicaoTipada"
                }
              ],
              "elseActions": [
                {
                  "id": "guardar-condicao-falsa",
                  "type": "setVariable",
                  "name": "Guardar condição falsa",
                  "value": false,
                  "target": "runtime.condicaoTipada"
                }
              ]
            },
            {
              "id": "repetir-tentativas",
              "type": "repeat",
              "name": "Repetir tentativas",
              "times": 2,
              "indexVariable": "tentativa",
              "actions": [
                {
                  "id": "guardar-tentativa",
                  "type": "setVariable",
                  "name": "Guardar tentativa",
                  "valueSource": "loop.tentativa",
                  "target": "runtime.ultimaTentativa"
                }
              ]
            },
            {
              "id": "percorrer-documentos",
              "type": "forEach",
              "name": "Percorrer documentos",
              "items": [
                { "codigo": 1 },
                { "codigo": 2 }
              ],
              "itemVariable": "documento",
              "indexVariable": "indiceDocumento",
              "actions": [
                {
                  "id": "executar-processamento-item",
                  "type": "runSubflow",
                  "name": "Executar processamento do item",
                  "subflow": "processarItem"
                }
              ]
            }
          ],
          "subflows": {
            "processarItem": [
              {
                "id": "guardar-documento-atual",
                "type": "setVariable",
                "name": "Guardar documento atual",
                "valueSource": "loop.documento",
                "target": "runtime.ultimoDocumento"
              }
            ]
          }
        }
        """;

    var exportedJson = await page.EvaluateAsync<string>(
        "flowJson => JSON.stringify(window.RpaFlowEditorTesting.roundTrip(JSON.parse(flowJson)))",
        fixture);
    var original = SerializeCanonical(
        DeserializeAndNormalize(fixture, "fixture-generalizada"));
    var exported = SerializeCanonical(
        DeserializeAndNormalize(exportedJson, "fixture-generalizada"));
    if (!string.Equals(original, exported, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "O round-trip Blockly alterou propriedades generalizadas.");
    }

    Console.WriteLine(
        "OK: round-trip preservou propriedades generalizadas e controle de fluxo.");
}

static async Task CheckSharedToolboxAsync(IPage page)
{
    var blocks = await page.EvaluateAsync<string[]>(
        "() => window.RpaFlowEditorTesting.toolboxBlockTypes()");
    var requiredBlocks = new[]
    {
        "rpa_navigate",
        "rpa_click",
        "rpa_click_optional",
        "rpa_wait",
        "rpa_fill",
        "rpa_select_option",
        "rpa_set_checked",
        "rpa_press_key",
        "rpa_type_sequentially",
        "rpa_type_across_inputs",
        "rpa_click_new_page",
        "rpa_upload",
        "rpa_wait_stable",
        "rpa_preserve_fill",
        "rpa_select2",
        "rpa_currency",
        "rpa_set_variable",
        "rpa_capture_timestamp",
        "rpa_wait_one_time_code",
        "rpa_read_element",
        "rpa_read_elements",
        "rpa_switch_page",
        "rpa_close_page",
        "rpa_screenshot",
        "rpa_download_click",
        "rpa_download_request",
        "rpa_safe_final",
        "rpa_fail",
        "rpa_complete_authentication_attempt",
        "rpa_transform_path",
        "rpa_if_value",
        "rpa_if_element",
        "rpa_repeat",
        "rpa_for_each",
        "rpa_run_subflow",
        "rpa_subflow_definition"
    };
    var missing = requiredBlocks.Where(block => !blocks.Contains(block)).ToArray();
    if (missing.Length > 0)
    {
        throw new InvalidOperationException(
            "A toolbox compartilhada ocultou blocos da biblioteca: " +
            string.Join(", ", missing));
    }

    var formBlocks = await page.EvaluateAsync<string[]>(
        "name => window.RpaFlowEditorTesting.toolboxCategoryBlockTypes(name)",
        "Formulários");
    var sequentialIndex = Array.IndexOf(formBlocks, "rpa_type_sequentially");
    var acrossInputsIndex = Array.IndexOf(formBlocks, "rpa_type_across_inputs");
    if (sequentialIndex < 0 || acrossInputsIndex != sequentialIndex + 1)
    {
        throw new InvalidOperationException(
            "A digitação segmentada não está ao lado da digitação sequencial em Formulários.");
    }

    var waitBlocks = await page.EvaluateAsync<string[]>(
        "name => window.RpaFlowEditorTesting.toolboxCategoryBlockTypes(name)",
        "Esperas");
    if (!waitBlocks.Contains("rpa_wait_one_time_code"))
    {
        throw new InvalidOperationException(
            "O bloco de espera do código de autenticação saiu da categoria Esperas.");
    }

    Console.WriteLine("OK: toolbox expôs a biblioteca completa para todos os RPAs.");
}

static FlowDefinition DeserializeAndNormalize(string json, string project)
{
    var flow = JsonSerializer.Deserialize<FlowDefinition>(
        json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException($"Fluxo vazio no round-trip de {project}.");
    FlowDefinitionValidator.Validate(flow);
    NormalizeLegacyArtifactNames(flow.Actions);
    foreach (var actions in flow.Subflows.Values)
    {
        NormalizeLegacyArtifactNames(actions);
    }
    return flow;
}

static void NormalizeLegacyArtifactNames(IEnumerable<FlowActionDefinition> actions)
{
    foreach (var action in actions)
    {
        if (string.IsNullOrWhiteSpace(action.FileName) &&
            !string.IsNullOrWhiteSpace(action.ScreenshotName))
        {
            action.FileName = action.ScreenshotName;
            action.ScreenshotName = null;
        }

        if (action.SeparateByExecution == true)
        {
            action.SeparateByExecution = null;
        }

        if (action.ConflictStrategy?.Equals(
                "unique",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            action.ConflictStrategy = null;
        }

        if (action.MatchMode?.Equals(
                "first",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            action.MatchMode = null;
        }

        if (action.Condition?.MatchMode?.Equals(
                "first",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            action.Condition.MatchMode = null;
        }

        NormalizeLegacyArtifactNames(action.Actions);
        NormalizeLegacyArtifactNames(action.ElseActions);
    }
}

static string SerializeCanonical(FlowDefinition flow) =>
    JsonSerializer.Serialize(flow, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    });

static Process StartEditor(string editorDll, string repositoryRoot, string editorUrl)
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
    startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "examples", "RpaExemplo"));
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
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"O editor de round-trip encerrou antes de iniciar: {error}");
        }

        try
        {
            using var response = await client.GetAsync($"{editorUrl}/api/session");
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }

        await Task.Delay(100);
    }

    throw new TimeoutException("O editor de round-trip não iniciou em 30 segundos.");
}

static int FindAvailablePort()
{
    var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}
