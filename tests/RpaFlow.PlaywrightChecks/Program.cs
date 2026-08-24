using System.Net;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RpaFlow.Contracts;
using RpaFlow.Playwright;
using RpaFlow.Playwright.V2;
using RpaFlow.Playwright.V2.Adaptive;
using RpaFlow.Packages;
using RpaFlow.Runtime;
using RpaFlow.Migrator;
using V2 = RpaFlow.Contracts.V2;

var innermostFrame =
    """
    <button id="botao-frame" onclick="document.getElementById('resultado-frame').textContent='clicado'">Dentro</button>
    <span id="resultado-frame">pendente</span>
    """;
var innerFrame =
    """
    <iframe id="sapPopupMainId_X0" src="about:blank?emptyhover.html" style="display:none"></iframe>
    <iframe id="sapPopupMainId_X1" src="about:blank?emptyhover.html" style="display:none"></iframe>
    """ +
    $"""<iframe id="quadro-interno" srcdoc="{WebUtility.HtmlEncode(innermostFrame)}"></iframe>""";
var framesUrl = DataUrl(
    "<!doctype html><html><head><title>Frames</title></head><body>" +
    $"""<iframe id="quadro-externo" srcdoc="{WebUtility.HtmlEncode(innerFrame)}"></iframe>""" +
    "</body></html>");

var originUrl = DataUrl(
    """
    <!doctype html>
    <html>
      <head><title>Origem</title></head>
      <body>
        <select id="tipo">
          <option value="">Selecione</option>
          <option value="servico">Serviço</option>
        </select>
        <input id="aceite" type="checkbox">
        <div style="display:none">
          <input id="trustedDevice" type="checkbox">
        </div>
        <label for="trustedDevice">Adicionar dispositivo como confiável por 7 dias.</label>
        <input id="pesquisa" onkeydown="if(event.key === 'Enter'){this.value='confirmado'}">
        <div id="pin">
          <input class="pin-segment" maxlength="1" value="9">
          <input class="pin-segment" maxlength="1" value="9">
          <input class="pin-segment" maxlength="1" value="9">
          <input class="pin-segment" maxlength="1" value="9">
          <input class="pin-segment" maxlength="1" value="9">
          <input class="pin-segment" maxlength="1" value="9">
          <input class="pin-segment" maxlength="1" value="oculto" style="display:none">
        </div>
        <ul><li class="item">Primeiro</li><li class="item">Segundo</li></ul>
        <button id="abrir-relatorio" onclick="
          const popup = window.open('about:blank');
          popup.document.title = 'Relatório';
          const main = popup.document.createElement('main');
          main.id = 'report';
          main.textContent = 'Relatório pronto';
          popup.document.body.append(main);">Abrir</button>
        <script>
          const pinInputs = Array.from(document.querySelectorAll('#pin .pin-segment'))
            .filter(input => input.offsetParent !== null);
          pinInputs.forEach((input, index) => input.addEventListener('input', () => {
            if (input.value && index + 1 < pinInputs.length) {
              pinInputs[index + 1].focus();
            }
          }));
        </script>
      </body>
    </html>
    """);

var otpRequestedAt = new DateTimeOffset(
    2026,
    7,
    30,
    13,
    45,
    10,
    TimeSpan.Zero);
var oneTimeCodeProvider = new FakeOneTimeCodeProvider(
    new OneTimeCodeResult("654321", otpRequestedAt.AddSeconds(8)));

var flow = new FlowDefinition
{
    SchemaVersion = 1,
    Name = "Teste local dos blocos Playwright",
    Actions =
    [
        new FlowActionDefinition
        {
            Id = "capturar-instante-otp",
            Type = "captureTimestamp",
            Name = "Capturar instante do pedido do código",
            Target = "runtime.authentication.otpRequestedAt"
        },
        new FlowActionDefinition
        {
            Id = "aguardar-otp",
            Type = "waitForOneTimeCode",
            Name = "Aguardar código de uso único",
            ProviderAlias = "email-otp",
            NotBeforeSource = "runtime.authentication.otpRequestedAt",
            Target = "runtime.authentication.otp",
            TimeoutMs = 120_000,
            PollIntervalMs = 5_000
        },
        Action("navegar", "navigate", "Abrir HTML local", value: originUrl),
        Action(
            "selecionar",
            "selectOption",
            "Selecionar opção nativa",
            selector: "#tipo",
            value: "Serviço",
            optionMode: "label"),
        Action(
            "marcar",
            "setChecked",
            "Marcar aceite",
            selector: "#aceite",
            value: true),
        new FlowActionDefinition
        {
            Id = "ler-dispositivo-confiavel-antes",
            Type = "readElement",
            Name = "Ler dispositivo confiável antes",
            Selector = "#trustedDevice",
            Property = "checked",
            Target = "runtime.trustedDeviceInitiallyChecked"
        },
        new FlowActionDefinition
        {
            Id = "marcar-dispositivo-confiavel-se-necessario",
            Type = "if",
            Name = "Marcar dispositivo confiável se necessário",
            Condition = new FlowConditionDefinition
            {
                Type = "value",
                LeftSource = "runtime.trustedDeviceInitiallyChecked",
                Operator = "equals",
                RightValue = JsonSerializer.SerializeToElement(false)
            },
            Actions =
            [
                Action(
                    "clicar-dispositivo-confiavel",
                    "click",
                    "Clicar no label do dispositivo confiável",
                    selector: "label[for='trustedDevice']"),
                new FlowActionDefinition
                {
                    Id = "confirmar-dispositivo-confiavel",
                    Type = "wait",
                    Name = "Confirmar dispositivo confiável",
                    Selector = "#trustedDevice:checked",
                    State = "attached",
                    TimeoutMs = 5_000
                }
            ]
        },
        new FlowActionDefinition
        {
            Id = "ler-dispositivo-confiavel-depois",
            Type = "readElement",
            Name = "Ler dispositivo confiável depois",
            Selector = "#trustedDevice",
            Property = "checked",
            Target = "runtime.trustedDeviceChecked"
        },
        Action(
            "teclar",
            "pressKey",
            "Pressionar Enter",
            selector: "#pesquisa",
            value: "Enter"),
        new FlowActionDefinition
        {
            Id = "digitar-pin-segmentado",
            Type = "typeAcrossInputs",
            Name = "Digitar PIN em campos segmentados",
            Selector = "#pin .pin-segment",
            ValueSource = "input.pin",
            DelayMs = 0,
            ClearFirst = true,
            BlurAfter = true
        },
        new FlowActionDefinition
        {
            Id = "ler-pin-segmentado",
            Type = "readElements",
            Name = "Ler PIN segmentado",
            Selector = "#pin .pin-segment:visible",
            Property = "value",
            MaxItems = 6,
            Target = "runtime.pinSegments"
        },
        new FlowActionDefinition
        {
            Id = "ler-itens",
            Type = "readElements",
            Name = "Ler itens",
            Selector = ".item",
            Property = "text",
            MaxItems = 10,
            Target = "runtime.itens"
        },
        new FlowActionDefinition
        {
            Id = "abrir-relatorio",
            Type = "clickAndSwitchPage",
            Name = "Abrir relatório",
            Selector = "#abrir-relatorio",
            ReadySelector = "#report"
        },
        Action(
            "voltar-origem",
            "switchPage",
            "Assumir origem",
            value: "Origem",
            property: "title",
            comparison: "exact",
            readySelector: "#pesquisa"),
        Action(
            "retornar-relatorio",
            "switchPage",
            "Assumir relatório",
            value: "Relatório",
            property: "title",
            comparison: "exact",
            readySelector: "#report"),
        new FlowActionDefinition
        {
            Id = "fechar-relatorio",
            Type = "closePage",
            Name = "Fechar relatório",
            ReadySelector = "#pesquisa"
        },
        new FlowActionDefinition
        {
            Id = "ler-pesquisa",
            Type = "readElement",
            Name = "Ler pesquisa",
            Selector = "#pesquisa",
            Property = "value",
            Target = "runtime.pesquisa"
        },
        new FlowActionDefinition
        {
            Id = "ler-aceite",
            Type = "readElement",
            Name = "Ler aceite",
            Selector = "#aceite",
            Property = "checked",
            Target = "runtime.aceite"
        },
        new FlowActionDefinition
        {
            Id = "verificar-pesquisa",
            Type = "if",
            Name = "Verificar pesquisa confirmada",
            Condition = new FlowConditionDefinition
            {
                Type = "value",
                LeftSource = "runtime.pesquisa",
                Operator = "equals",
                RightValue = JsonSerializer.SerializeToElement("confirmado")
            },
            Actions =
            [
                new FlowActionDefinition
                {
                    Id = "registrar-condicao",
                    Type = "setVariable",
                    Name = "Registrar condição verdadeira",
                    Value = JsonSerializer.SerializeToElement(true),
                    Target = "runtime.condicaoExecutada"
                }
            ],
            ElseActions =
            [
                Action(
                    "falhar-condicao",
                    "fail",
                    "Falhar se a condição estiver incorreta",
                    value: "A condição por valor não foi executada.")
            ]
        },
        new FlowActionDefinition
        {
            Id = "verificar-lista-tipada",
            Type = "if",
            Name = "Verificar lista JSON tipada",
            Condition = new FlowConditionDefinition
            {
                Type = "value",
                LeftValue = JsonSerializer.SerializeToElement(
                    new object[] { "texto", 2 }),
                Operator = "contains",
                RightValue = JsonSerializer.SerializeToElement(2)
            },
            Actions =
            [
                new FlowActionDefinition
                {
                    Id = "registrar-condicao-tipada",
                    Type = "setVariable",
                    Name = "Registrar condição tipada verdadeira",
                    Value = JsonSerializer.SerializeToElement(true),
                    Target = "runtime.condicaoTipadaExecutada"
                }
            ],
            ElseActions =
            [
                Action(
                    "falhar-condicao-tipada",
                    "fail",
                    "Falhar se a condição tipada estiver incorreta",
                    value: "A condição com lista e número não foi executada.")
            ]
        },
        new FlowActionDefinition
        {
            Id = "repetir-tentativas",
            Type = "repeat",
            Name = "Repetir tentativas",
            Times = 3,
            IndexVariable = "tentativa",
            Actions =
            [
                new FlowActionDefinition
                {
                    Id = "registrar-tentativa",
                    Type = "setVariable",
                    Name = "Registrar tentativa atual",
                    ValueSource = "loop.tentativa",
                    Target = "runtime.ultimaTentativa"
                }
            ]
        },
        new FlowActionDefinition
        {
            Id = "percorrer-documentos",
            Type = "forEach",
            Name = "Percorrer documentos",
            Items =
            [
                JsonSerializer.SerializeToElement(new
                {
                    codigo = 1,
                    arquivos = new[] { "a.pdf", "b.xml" }
                }),
                JsonSerializer.SerializeToElement(new
                {
                    codigo = 2,
                    arquivos = new[] { "c.pdf" }
                })
            ],
            ItemVariable = "documento",
            IndexVariable = "indiceDocumento",
            Actions =
            [
                new FlowActionDefinition
                {
                    Id = "percorrer-arquivos",
                    Type = "forEach",
                    Name = "Percorrer arquivos do documento",
                    ItemsSource = "loop.documento.arquivos",
                    ItemVariable = "arquivo",
                    IndexVariable = "indiceArquivo",
                    Actions =
                    [
                        new FlowActionDefinition
                        {
                            Id = "executar-processamento-arquivo",
                            Type = "runSubflow",
                            Name = "Executar processamento do arquivo",
                            Subflow = "processarArquivo"
                        }
                    ]
                }
            ]
        }
    ],
    Subflows = new Dictionary<string, List<FlowActionDefinition>>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["processarArquivo"] =
        [
            new FlowActionDefinition
            {
                Id = "registrar-arquivo-atual",
                Type = "setVariable",
                Name = "Registrar arquivo atual",
                ValueSource = "loop.arquivo",
                Target = "runtime.ultimoArquivo"
            },
            new FlowActionDefinition
            {
                Id = "registrar-documento-atual",
                Type = "setVariable",
                Name = "Registrar documento atual",
                ValueSource = "loop.documento.codigo",
                Target = "runtime.ultimoDocumentoCodigo"
            }
        ]
    }
};

flow.Actions.AddRange(
    Action("abrir-frames", "navigate", "Abrir página com iframes aninhados", value: framesUrl),
    new FlowActionDefinition
    {
        Id = "clicar-botao-frame",
        Type = "click",
        Name = "Clicar no botão dentro de dois iframes",
        Selector = "#botao-frame",
        FrameSelectors =
        [
            "#quadro-externo",
            "#quadro-interno"
        ]
    },
    new FlowActionDefinition
    {
        Id = "ler-resultado-frame",
        Type = "readElement",
        Name = "Ler resultado do clique dentro dos iframes",
        Selector = "#resultado-frame",
        Property = "text",
        FrameSelectors =
        [
            "#quadro-externo",
            "#quadro-interno"
        ],
        Target = "runtime.resultadoFrame"
    });

FlowDefinitionValidator.Validate(flow);
var request = new FlowExecutionRequest(
    "playwright-local",
    new JsonObject { ["pin"] = "123456" },
    [],
    []);
var storageStatePath = Path.Combine(
    Directory.GetCurrentDirectory(),
    "tmp",
    "playwright-runtime-checks",
    $"storage-state-{Guid.NewGuid():N}.json");
var options = new PlaywrightRuntimeOptions(
    Headless: true,
    Browser: Environment.GetEnvironmentVariable("RPABLOCKLY_CHECKS_BROWSER") ?? "chromium",
    ActionTimeoutSeconds: 15,
    UploadTimeoutSeconds: 15,
    OutputDirectory: "tmp/playwright-runtime-checks",
    ConfigurationDirectory: Directory.GetCurrentDirectory(),
    StorageStatePath: storageStatePath,
    SaveStorageState: true);
var result = await new PlaywrightFlowExecutor(
        flow,
        options,
        oneTimeCodeProvider: oneTimeCodeProvider,
        timeProvider: new FixedTimeProvider(otpRequestedAt))
    .ExecuteAsync(request, CancellationToken.None);

var items = result.Output["itens"] as JsonArray;
var pinSegments = result.Output["pinSegments"] as JsonArray;
if (items is null || items.Count != 2 ||
    items[0]?.GetValue<string>() != "Primeiro" ||
    items[1]?.GetValue<string>() != "Segundo" ||
    result.Output["pesquisa"]?.GetValue<string>() != "confirmado" ||
    result.Output["aceite"]?.GetValue<bool>() != true ||
    result.Output["trustedDeviceInitiallyChecked"]?.GetValue<bool>() != false ||
    result.Output["trustedDeviceChecked"]?.GetValue<bool>() != true ||
    result.Output["condicaoExecutada"]?.GetValue<bool>() != true ||
    result.Output["condicaoTipadaExecutada"]?.GetValue<bool>() != true ||
    result.Output["ultimaTentativa"]?.GetValue<int>() != 2 ||
    result.Output["ultimoArquivo"]?.GetValue<string>() != "c.pdf" ||
    result.Output["ultimoDocumentoCodigo"]?.GetValue<int>() != 2 ||
    result.Output["resultadoFrame"]?.GetValue<string>() != "clicado" ||
    result.Output["authentication"]?["otpRequestedAt"]?.GetValue<string>() !=
        otpRequestedAt.ToString("O") ||
    result.Output["authentication"]?["otp"]?.GetValue<string>() != "654321" ||
    pinSegments is null ||
    string.Concat(pinSegments.Select(segment => segment?.GetValue<string>())) != "123456" ||
    result.ExecutedActions != 43)
{
    throw new InvalidOperationException(
        "Os blocos Playwright não produziram o resultado local esperado.");
}

if (!File.Exists(storageStatePath) ||
    JsonNode.Parse(await File.ReadAllTextAsync(storageStatePath)) is not JsonObject storageState ||
    storageState["cookies"] is not JsonArray ||
    storageState["origins"] is not JsonArray)
{
    throw new InvalidOperationException(
        "O estado do navegador não foi gravado em JSON após a execução bem-sucedida.");
}

var oneTimeCodeRequest = oneTimeCodeProvider.Requests.SingleOrDefault();
if (oneTimeCodeRequest is null ||
    oneTimeCodeRequest.ProviderAlias != "email-otp" ||
    oneTimeCodeRequest.NotBefore != otpRequestedAt ||
    oneTimeCodeRequest.Timeout != TimeSpan.FromMinutes(2) ||
    oneTimeCodeRequest.PollInterval != TimeSpan.FromSeconds(5))
{
    throw new InvalidOperationException(
        "O handler Playwright não propagou corretamente a espera ao provider falso.");
}

var migrated = new V1ToV2Migrator().Migrate(flow, "fixture-playwright-v1.json");
var migratedHash = CanonicalJson.ComputePackageHash(migrated.Documents);
var migratedSnapshot = new RpaPackageSnapshot(
    "fixture-diferencial",
    new PackageRevision(migratedHash),
    migrated.Documents,
    new RpaPackageOrigin("inline", "teste-diferencial"));
var migratedProvider = new FakeOneTimeCodeProvider(
    new OneTimeCodeResult("654321", otpRequestedAt.AddSeconds(8)));
var migratedResult = await new PlaywrightV2FlowExecutor(
        migratedSnapshot,
        options with { StorageStatePath = null, SaveStorageState = false },
        oneTimeCodeProvider: migratedProvider,
        timeProvider: new FixedTimeProvider(otpRequestedAt))
    .ExecuteAsync(request with { ExecutionId = "playwright-migrado-v2" }, CancellationToken.None);
if (!JsonNode.DeepEquals(result.Output, migratedResult.Output) ||
    result.ExecutedActions != migratedResult.ExecutedActions)
{
    throw new InvalidOperationException(
        "A execução strict do pacote migrado divergiu da mesma fixture V1.");
}
Console.WriteLine("OK: execução diferencial V1/V2 preservou output e ações observáveis.");

await CheckExecutionGuardAsync(options);
await CheckAfterActionCompletionAsync(options);
await CheckTypeAcrossInputsCardinalityAsync(options, originUrl);
await CheckV2LocatorResolverAsync(options.Browser);
CheckAdaptiveReferenceGolden();
await CheckLocatorLearningAsync();
await CheckV2FlowExecutorAsync(options, originUrl);
await CheckLearningDiagnosticsAsync(options);
await CheckArtifactHardeningAsync(options.Browser);
CheckV2LocatorArchitecture();

Console.WriteLine(
    $"OK: blocos web, guards antes/depois da ação, if, repeat, forEach aninhado, " +
    $"subfluxo e cadeia de " +
    $"iframes estável entre frames auxiliares funcionaram em HTML local com " +
    $"o navegador '{options.Browser}'.");

static async Task CheckV2LocatorResolverAsync(string browserName)
{
    var html =
        """
        <!doctype html>
        <html>
          <body>
            <section id="painel" data-testid="painel-principal">
              <h2>Área segura</h2>
              <label for="email">E-mail</label>
              <input id="email" name="email" placeholder="nome@exemplo.com">
              <button id="submit" data-testid="enviar">Enviar</button>
              <button class="secondary">Cancelar</button>
              <button id="save-new" name="save-order" class="primary-v2"
                aria-label="Salvar pedido">Salvar pedido</button>
              <button class="duplicate">Duplicado</button>
              <button class="duplicate">Duplicado</button>
              <span class="message">Mensagem exata</span>
              <ul><li class="item">Um</li><li class="item">Dois</li></ul>
            </section>
          </body>
        </html>
        """;
    using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    await using var browser = browserName.ToLowerInvariant() switch
    {
        "firefox" => await playwright.Firefox.LaunchAsync(
            new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true }),
        "webkit" => await playwright.Webkit.LaunchAsync(
            new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true }),
        _ => await playwright.Chromium.LaunchAsync(
            new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true })
    };
    var page = await browser.NewPageAsync();
    await page.GotoAsync(DataUrl(html));
    var data = new FlowDataContext(new FlowExecutionRequest(
        "resolver-v2",
        new JsonObject { ["area"] = "Área segura" },
        [],
        []));

    var catalog = new V2.LocatorCatalog
    {
        Locators =
        [
            Locator("css", V2.LocatorStrategy.Css, "#submit"),
            Locator("xpath", V2.LocatorStrategy.XPath, "//button[@id='submit']"),
            Locator("role", V2.LocatorStrategy.Role, role: "button", name: "Enviar"),
            Locator("label", V2.LocatorStrategy.Label, text: "E-mail"),
            Locator(
                "placeholder",
                V2.LocatorStrategy.Placeholder,
                text: "nome@exemplo.com"),
            Locator("text", V2.LocatorStrategy.Text, text: "Mensagem exata"),
            Locator("testid", V2.LocatorStrategy.TestId, text: "enviar"),
            Locator("raw", V2.LocatorStrategy.RawPlaywright, "button#submit"),
            new V2.LocatorDefinition
            {
                Id = "scoped",
                DisplayName = "Botão com escopo dinâmico",
                Candidates =
                [
                    new V2.LocatorCandidate
                    {
                        Id = "scoped-primary",
                        Origin = V2.LocatorCandidateOrigin.Developer,
                        DeveloperRole = V2.DeveloperLocatorRole.Original,
                        OriginalOrder = 0,
                        Recipe = new V2.LocatorRecipe
                        {
                            Scope = new V2.LocatorExpression
                            {
                                Strategy = V2.LocatorStrategy.Css,
                                Selector = "section",
                                HasText = new V2.LocatorTextConstraint
                                {
                                    Source = "input.area"
                                }
                            },
                            Target = new V2.LocatorExpression
                            {
                                Strategy = V2.LocatorStrategy.Role,
                                Role = "button",
                                Name = "Enviar",
                                Exact = true
                            }
                        }
                    }
                ]
            },
            new V2.LocatorDefinition
            {
                Id = "fallback",
                DisplayName = "Fallback ordenado",
                Candidates =
                [
                    Candidate("fallback-missing", "#ausente", 0),
                    Candidate("fallback-working", "#submit", 1)
                ]
            },
            new V2.LocatorDefinition
            {
                Id = "all-invalid",
                DisplayName = "Todos inválidos",
                Candidates =
                [
                    Candidate("invalid-one", "#ausente-um", 0),
                    Candidate("invalid-two", "#ausente-dois", 1)
                ]
            },
            Locator("ambiguous", V2.LocatorStrategy.Css, "button"),
            Locator("many", V2.LocatorStrategy.Css, "li.item"),
            new V2.LocatorDefinition
            {
                Id = "adaptive",
                DisplayName = "Alvo alterado",
                Candidates = [Candidate("adaptive-old", "#save-old", 0)],
                Fingerprints =
                [
                    new V2.LocatorFingerprint
                    {
                        Id = "adaptive-original",
                        TagName = "button",
                        AccessibleName = "Salvar pedido",
                        Text = "Salvar pedido",
                        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["id"] = "save-old",
                            ["name"] = "save-order",
                            ["class"] = "primary"
                        },
                        Ancestors =
                        [
                            new V2.LocatorFingerprintNode
                            {
                                TagName = "section",
                                Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    ["id"] = "painel"
                                }
                            }
                        ]
                    }
                ]
            },
            new V2.LocatorDefinition
            {
                Id = "adaptive-tie",
                DisplayName = "Empate adaptativo",
                Candidates = [Candidate("tie-old", "#duplicate-old", 0)],
                Fingerprints =
                [
                    new V2.LocatorFingerprint
                    {
                        Id = "tie-original",
                        TagName = "button",
                        Text = "Duplicado",
                        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["class"] = "duplicate"
                        }
                    }
                ]
            }
        ]
    };
    var strictPolicy = new V2.RpaPolicyDefinition();
    var strictResolver = new LocatorResolver(catalog, strictPolicy);
    foreach (var locatorId in new[]
             {
                 "css", "xpath", "role", "label", "placeholder", "text",
                 "testid", "raw", "scoped"
             })
    {
        var resolved = await strictResolver.ResolveAsync(
            page,
            new V2.LocatorUseDefinition
            {
                LocatorId = locatorId,
                Cardinality = V2.LocatorCardinality.Single
            },
            data,
            new LocatorResolutionRequirement(LocatorRequiredState.Visible),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        if (resolved.Attempts.Count != 1 || !resolved.Attempts[0].Succeeded)
        {
            throw new InvalidOperationException(
                $"A estratégia V2 '{locatorId}' não resolveu em modo strict.");
        }
    }

    const int loadIterations = 100;
    var loadWatch = Stopwatch.StartNew();
    for (var index = 0; index < loadIterations; index++)
    {
        var resolved = await strictResolver.ResolveAsync(
            page,
            new V2.LocatorUseDefinition
            {
                LocatorId = "css",
                Cardinality = V2.LocatorCardinality.Single
            },
            data,
            new LocatorResolutionRequirement(LocatorRequiredState.Visible),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        if (resolved.Attempts.Count != 1 || !resolved.Attempts[0].Succeeded)
        {
            throw new InvalidOperationException(
                "O teste de carga do resolver perdeu a resolução estrita.");
        }
    }

    loadWatch.Stop();
    if (loadWatch.Elapsed > TimeSpan.FromSeconds(30))
    {
        throw new InvalidOperationException(
            $"Cem resoluções estritas excederam 30 s: {loadWatch.Elapsed}.");
    }
    Console.WriteLine(
        $"OK: {loadIterations} resoluções estritas concluídas em " +
        $"{loadWatch.ElapsedMilliseconds} ms.");

    var fallbackPolicy = new V2.RpaPolicyDefinition
    {
        LocatorResilience = new V2.LocatorResiliencePolicy
        {
            Mode = V2.LocatorResilienceMode.Fallback,
            MaximumResolutionMilliseconds = 3_000
        }
    };
    var resolutionObserver = new RecordingFlowExecutionObserver();
    var fallbackResolver = new LocatorResolver(
        catalog,
        fallbackPolicy,
        observer: resolutionObserver);
    var fallback = await fallbackResolver.ResolveAsync(
        page,
        new V2.LocatorUseDefinition
        {
            LocatorId = "fallback",
            Cardinality = V2.LocatorCardinality.Single
        },
        data,
        new LocatorResolutionRequirement(LocatorRequiredState.Visible),
        TimeSpan.FromSeconds(3),
        CancellationToken.None);
    if (fallback.Candidate.Id != "fallback-working" ||
        fallback.Attempts.Count != 2 || fallback.Attempts[0].Succeeded)
    {
        throw new InvalidOperationException("O fallback V2 não preservou a ordem.");
    }

    var first = await fallbackResolver.ResolveAsync(
        page,
        new V2.LocatorUseDefinition
        {
            LocatorId = "ambiguous",
            Cardinality = V2.LocatorCardinality.First
        },
        data,
        new LocatorResolutionRequirement(LocatorRequiredState.Visible),
        TimeSpan.FromSeconds(2),
        CancellationToken.None);
    if (await first.Locator.CountAsync() != 1)
    {
        throw new InvalidOperationException("A cardinalidade first não materializou um alvo.");
    }

    var many = await fallbackResolver.ResolveAsync(
        page,
        new V2.LocatorUseDefinition
        {
            LocatorId = "many",
            Cardinality = V2.LocatorCardinality.Many
        },
        data,
        new LocatorResolutionRequirement(LocatorRequiredState.Attached),
        TimeSpan.FromSeconds(2),
        CancellationToken.None);
    if (await many.Locator.CountAsync() != 2)
    {
        throw new InvalidOperationException("A cardinalidade many perdeu a coleção.");
    }

    try
    {
        await strictResolver.ResolveAsync(
            page,
            new V2.LocatorUseDefinition
            {
                LocatorId = "ambiguous",
                Cardinality = V2.LocatorCardinality.Single
            },
            data,
            new LocatorResolutionRequirement(LocatorRequiredState.Visible),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        throw new InvalidOperationException("Locator ambíguo foi aceito como single.");
    }
    catch (LocatorResolutionException exception)
        when (exception.Attempts.Single().FailureReason ==
              LocatorResolutionFailureReason.Ambiguous)
    {
    }

    try
    {
        await fallbackResolver.ResolveAsync(
            page,
            new V2.LocatorUseDefinition
            {
                LocatorId = "all-invalid",
                Cardinality = V2.LocatorCardinality.Single
            },
            data,
            new LocatorResolutionRequirement(LocatorRequiredState.Visible),
            TimeSpan.FromMilliseconds(250),
            CancellationToken.None);
        throw new InvalidOperationException("Fallback aceitou todos os candidatos inválidos.");
    }
    catch (LocatorResolutionException exception)
        when (exception.Attempts.Count == 2 &&
              exception.Attempts.All(attempt => !attempt.Succeeded))
    {
    }

    var adaptivePolicy = new V2.RpaPolicyDefinition
    {
        LocatorResilience = new V2.LocatorResiliencePolicy
        {
            Mode = V2.LocatorResilienceMode.Adaptive,
            LearningWriteBack = V2.LearningWriteBackMode.Memory,
            Promotion = V2.LocatorPromotionMode.AfterSuccessfulExecution,
            MinimumConfidence = 0.40,
            MinimumRunnerUpGap = 0.08,
            MaximumHeuristicNodes = 500,
            MaximumResolutionMilliseconds = 3_000
        }
    };
    var adaptiveResolver = new LocatorResolver(
        catalog,
        adaptivePolicy,
        observer: resolutionObserver);
    var adaptive = await adaptiveResolver.ResolveAsync(
        page,
        new V2.LocatorUseDefinition
        {
            LocatorId = "adaptive",
            Cardinality = V2.LocatorCardinality.Single
        },
        data,
        new LocatorResolutionRequirement(LocatorRequiredState.Visible),
        TimeSpan.FromSeconds(3),
        CancellationToken.None);
    var adaptiveAgain = await adaptiveResolver.ResolveAsync(
        page,
        new V2.LocatorUseDefinition
        {
            LocatorId = "adaptive",
            Cardinality = V2.LocatorCardinality.Single
        },
        data,
        new LocatorResolutionRequirement(LocatorRequiredState.Visible),
        TimeSpan.FromSeconds(3),
        CancellationToken.None);
    if (!adaptive.UsedHeuristic || adaptive.LearnedFingerprint is null ||
        await adaptive.Locator.GetAttributeAsync("id") != "save-new" ||
        adaptive.Candidate.Id != adaptiveAgain.Candidate.Id ||
        adaptive.Confidence != adaptiveAgain.Confidence)
    {
        throw new InvalidOperationException(
            "A heurística V2 não recuperou o mesmo alvo de forma determinística.");
    }

    try
    {
        await adaptiveResolver.ResolveAsync(
            page,
            new V2.LocatorUseDefinition
            {
                LocatorId = "adaptive-tie",
                Cardinality = V2.LocatorCardinality.Single
            },
            data,
            new LocatorResolutionRequirement(LocatorRequiredState.Visible),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        throw new InvalidOperationException("A heurística V2 aceitou um empate.");
    }
    catch (LocatorResolutionException exception)
        when (exception.Attempts.Last().FailureReason ==
              LocatorResolutionFailureReason.Ambiguous)
    {
    }

    var expectedDiagnosticKinds = new[]
    {
        "locatorResolutionStarted",
        "locatorCandidateAccepted",
        "locatorCandidateRejected",
        "locatorResolutionCompleted",
        "locatorResolutionFailed"
    };
    if (expectedDiagnosticKinds.Any(kind =>
            !resolutionObserver.Events.Any(item => item.Kind == kind)) ||
        resolutionObserver.Events.Any(item =>
            item.ExecutionId != "resolver-v2" ||
            item.Detail?.Contains("nome@exemplo.com", StringComparison.Ordinal) == true))
    {
        throw new InvalidOperationException(
            "Os diagnósticos do resolver não cobriram o ciclo completo ou expuseram dados.");
    }

    await page.CloseAsync();
    try
    {
        await fallbackResolver.ResolveAsync(
            page,
            new V2.LocatorUseDefinition
            {
                LocatorId = "fallback",
                Cardinality = V2.LocatorCardinality.Single
            },
            data,
            new LocatorResolutionRequirement(LocatorRequiredState.Visible),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        throw new InvalidOperationException("Resolver aceitou uma página encerrada.");
    }
    catch (LocatorResolutionException exception)
        when (exception.Attempts.Count == 1 &&
              exception.Attempts[0].FailureReason ==
                  LocatorResolutionFailureReason.PageOrContextClosed)
    {
    }

    Console.WriteLine(
        "OK: resolver V2 compilou oito estratégias exatas, scope dinâmico, " +
        "fallback ordenado, heurística determinística e cardinalidades seguras.");
}

static async Task CheckV2FlowExecutorAsync(
    PlaywrightRuntimeOptions options,
    string originUrl)
{
    static V2.LocatorUseDefinition Use(
        string id,
        V2.LocatorCardinality cardinality = V2.LocatorCardinality.Single) =>
        new() { LocatorId = id, Cardinality = cardinality };

    static V2.LocatorDefinition Locator(string id, string selector) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Candidates =
            [
                new V2.LocatorCandidate
                {
                    Id = id + ".primary",
                    Origin = V2.LocatorCandidateOrigin.Developer,
                    DeveloperRole = V2.DeveloperLocatorRole.Original,
                    OriginalOrder = 0,
                    Recipe = new V2.LocatorRecipe
                    {
                        Target = new V2.LocatorExpression
                        {
                            Strategy = V2.LocatorStrategy.Css,
                            Selector = selector
                        }
                    }
                }
            ]
        };

    var definition = new V2.FlowDefinition
    {
        Name = "Execução V2 estrita",
        Inputs =
        [
            new V2.FlowInputRequirementDefinition
            {
                Path = "input.pin",
                Type = "string"
            }
        ],
        Actions =
        [
            new V2.FlowActionDefinition
            {
                Id = "v2-navigate",
                Type = "navigate",
                Name = "Abrir fixture V2",
                Value = JsonSerializer.SerializeToElement(originUrl)
            },
            new V2.FlowActionDefinition
            {
                Id = "v2-select",
                Type = "selectOption",
                Name = "Selecionar tipo",
                Target = Use("tipo"),
                OptionMode = "value",
                Value = JsonSerializer.SerializeToElement("servico")
            },
            new V2.FlowActionDefinition
            {
                Id = "v2-check",
                Type = "setChecked",
                Name = "Marcar aceite",
                Target = Use("aceite"),
                Value = JsonSerializer.SerializeToElement(true)
            },
            new V2.FlowActionDefinition
            {
                Id = "v2-pin",
                Type = "typeAcrossInputs",
                Name = "Preencher PIN",
                Target = Use("pin", V2.LocatorCardinality.Many),
                ValueSource = "input.pin",
                ClearFirst = true,
                DelayMs = 0
            },
            new V2.FlowActionDefinition
            {
                Id = "v2-read-items",
                Type = "readElements",
                Name = "Ler itens",
                Target = Use("items", V2.LocatorCardinality.Many),
                Property = "text",
                Output = "runtime.items"
            },
            new V2.FlowActionDefinition
            {
                Id = "v2-if",
                Type = "if",
                Name = "Validar lista",
                Condition = new V2.FlowConditionDefinition
                {
                    Type = "value",
                    LeftSource = "runtime.items",
                    Operator = "contains",
                    RightValue = JsonSerializer.SerializeToElement("Segundo")
                },
                Actions =
                [
                    new V2.FlowActionDefinition
                    {
                        Id = "v2-repeat",
                        Type = "repeat",
                        Name = "Repetir marcação",
                        Times = 2,
                        Actions =
                        [
                            new V2.FlowActionDefinition
                            {
                                Id = "v2-set-output",
                                Type = "setVariable",
                                Name = "Registrar resultado",
                                Value = JsonSerializer.SerializeToElement("ok"),
                                Output = "runtime.status"
                            }
                        ]
                    }
                ]
            },
            new V2.FlowActionDefinition
            {
                Id = "v2-read-check",
                Type = "readElement",
                Name = "Ler aceite",
                Target = Use("aceite"),
                Property = "checked",
                Output = "runtime.checked"
            }
        ]
    };
    var catalog = new V2.LocatorCatalog
    {
        Locators =
        [
            Locator("tipo", "#tipo"),
            Locator("aceite", "#aceite"),
            Locator("pin", "#pin .pin-segment:not([style*='display:none'])"),
            Locator("items", ".item")
        ]
    };
    var policy = new V2.RpaPolicyDefinition
    {
        LocatorResilience = new V2.LocatorResiliencePolicy
        {
            Mode = V2.LocatorResilienceMode.Strict,
            MaximumResolutionMilliseconds = 15_000
        }
    };
    var documents = new RpaPackageDocuments(definition, catalog, policy);
    var snapshot = new RpaPackageSnapshot(
        "fixture-v2",
        new PackageRevision("fixture-v2-r1"),
        documents,
        new RpaPackageOrigin("test", "memory"));
    var observer = new RecordingFlowExecutionObserver();
    var result = await new PlaywrightV2FlowExecutor(snapshot, options, observer)
        .ExecuteAsync(
            new FlowExecutionRequest(
                "playwright-v2-local",
                new JsonObject { ["pin"] = "123456" },
                [],
                []),
            CancellationToken.None);

    if (result.Output["status"]?.GetValue<string>() != "ok" ||
        result.Output["checked"]?.GetValue<bool>() != true ||
        result.Output["items"] is not JsonArray items ||
        items.Count != 2 ||
        result.ExecutedActions != 10)
    {
        throw new InvalidOperationException(
            "O executor V2 estrito não preservou o comportamento observável esperado.");
    }

    var supported = V2FlowActionHandlerRegistry.Default.SupportedTypes;
    if (!supported.SetEquals(FlowActionCatalog.SupportedTypes))
    {
        throw new InvalidOperationException(
            "Os handlers V2 não cobrem exatamente os 33 tipos do catálogo.");
    }


    if (!observer.Events.Any(item =>
            item.Kind == "locatorResolutionCompleted" &&
            item.RpaId == "fixture-v2" &&
            item.PackageRevision == "fixture-v2-r1" &&
            item.PackageHash == snapshot.ContentHash &&
            item.LocatorId == "tipo"))
    {
        throw new InvalidOperationException(
            "O diagnóstico V2 não registrou RPA, revisão, hash e locator.");
    }
}

static async Task CheckLearningDiagnosticsAsync(PlaywrightRuntimeOptions options)
{
    var url = DataUrl(
        "<!doctype html><button id='submit-new' name='submit-order' " +
        "aria-label='Enviar pedido'>Enviar pedido</button>");
    var documents = new RpaPackageDocuments(
        new V2.FlowDefinition
        {
            Name = "Diagnóstico de promoção",
            Actions =
            [
                new V2.FlowActionDefinition
                {
                    Id = "open",
                    Type = "navigate",
                    Name = "Abrir",
                    Value = JsonSerializer.SerializeToElement(url)
                },
                new V2.FlowActionDefinition
                {
                    Id = "submit",
                    Type = "click",
                    Name = "Enviar",
                    Target = new V2.LocatorUseDefinition
                    {
                        LocatorId = "submit",
                        Cardinality = V2.LocatorCardinality.Single
                    }
                }
            ]
        },
        new V2.LocatorCatalog
        {
            Locators =
            [
                new V2.LocatorDefinition
                {
                    Id = "submit",
                    DisplayName = "Enviar pedido",
                    Candidates = [Candidate("submit-old", "#submit-old", 0)],
                    Fingerprints =
                    [
                        new V2.LocatorFingerprint
                        {
                            Id = "submit-original",
                            TagName = "button",
                            AccessibleName = "Enviar pedido",
                            Text = "Enviar pedido",
                            Attributes = new Dictionary<string, string>
                            {
                                ["id"] = "submit-old",
                                ["name"] = "submit-order"
                            }
                        }
                    ]
                }
            ]
        },
        new V2.RpaPolicyDefinition
        {
            LocatorResilience = new V2.LocatorResiliencePolicy
            {
                Mode = V2.LocatorResilienceMode.Adaptive,
                LearningWriteBack = V2.LearningWriteBackMode.Memory,
                Promotion = V2.LocatorPromotionMode.AfterSuccessfulExecution,
                MinimumConfidence = 0.35,
                MinimumRunnerUpGap = 0.05,
                MaximumHeuristicNodes = 100,
                MaximumResolutionMilliseconds = 3_000
            }
        });
    var snapshot = new RpaPackageSnapshot(
        "learning-events",
        new PackageRevision("learning-events-r1"),
        documents,
        new RpaPackageOrigin("test", "memory"));
    var observer = new RecordingFlowExecutionObserver();
    await new PlaywrightV2FlowExecutor(snapshot, options, observer)
        .ExecuteAsync(
            new FlowExecutionRequest("learning-events-execution", [], [], []),
            CancellationToken.None);
    if (!observer.Events.Any(item =>
            item.Kind == "locatorPromotionCompleted" &&
            item.ExecutionId == "learning-events-execution" &&
            item.RpaId == "learning-events" &&
            item.LocatorId == "submit" &&
            !string.IsNullOrWhiteSpace(item.CandidateId)))
    {
        throw new InvalidOperationException(
            "A promoção confirmada não emitiu o diagnóstico completo esperado.");
    }
}

static async Task CheckArtifactHardeningAsync(string browserName)
{
    var repositoryRoot = Directory.GetCurrentDirectory();
    var root = Path.Combine(
        repositoryRoot,
        "tmp",
        "artifact-hardening",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var expired = Path.Combine(root, "20200101-000000000-expired");
        Directory.CreateDirectory(expired);
        await File.WriteAllTextAsync(Path.Combine(expired, "old.txt"), "antigo");
        Directory.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-60));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = browserName.ToLowerInvariant() switch
        {
            "firefox" => await playwright.Firefox.LaunchAsync(
                new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true }),
            "webkit" => await playwright.Webkit.LaunchAsync(
                new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true }),
            _ => await playwright.Chromium.LaunchAsync(
                new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true })
        };
        var page = await browser.NewPageAsync();
        await page.SetContentAsync(
            "<!doctype html><input type='password' value='segredo-super'>" +
            "<div data-private>texto-confidencial</div>" +
            "<a href='https://example.invalid/path?token=segredo'>Link</a>");

        var sizeLimited = new ExecutionArtifacts(
            page,
            root,
            "size",
            maximumArtifactBytes: 5,
            maximumFiles: 10,
            retention: TimeSpan.FromDays(30));
        if (Directory.Exists(expired))
        {
            throw new InvalidOperationException("A retenção não removeu artefato expirado.");
        }

        await sizeLimited.SaveBytesAsync([1, 2, 3, 4, 5], "ok.bin");
        await ExpectInvalidAsync(
            () => sizeLimited.SaveBytesAsync([1, 2, 3, 4, 5, 6], "large.bin"),
            "artefato acima do limite é recusado e removido");

        var countLimited = new ExecutionArtifacts(
            page,
            root,
            "count",
            maximumArtifactBytes: 1_024,
            maximumFiles: 1);
        await countLimited.SaveBytesAsync([1], "first.bin");
        await ExpectInvalidAsync(
            () => countLimited.SaveBytesAsync([2], "second.bin"),
            "quantidade máxima de artefatos é respeitada");

        var diagnostics = new ExecutionArtifacts(
            page,
            root,
            "diagnostics",
            maximumArtifactBytes: 2 * 1024 * 1024,
            maximumFiles: 10);
        var captured = await diagnostics.CaptureFailureDiagnosticsAsync(
            new InvalidOperationException("falha sanitizada"));
        var html = await File.ReadAllTextAsync(captured.SanitizedHtmlPath!);
        if (html.Contains("segredo-super", StringComparison.Ordinal) ||
            html.Contains("texto-confidencial", StringComparison.Ordinal) ||
            html.Contains("?token=segredo", StringComparison.Ordinal) ||
            !html.Contains("[CONTEÚDO REDIGIDO]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "O HTML diagnóstico não redigiu conteúdo sensível.");
        }
    }
    finally
    {
        var fullRoot = Path.GetFullPath(root);
        var allowedRoot = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            "tmp",
            "artifact-hardening"));
        if (!fullRoot.StartsWith(
                allowedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Diretório de artefatos saiu da área permitida.");
        }

        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }
}

static async Task ExpectInvalidAsync(Func<Task> action, string description)
{
    try
    {
        await action();
    }
    catch (InvalidOperationException)
    {
        Console.WriteLine($"OK: {description}.");
        return;
    }

    throw new InvalidOperationException($"Falha: {description}.");
}

static void CheckAdaptiveReferenceGolden()
{
    var goldenPath = Path.Combine(
        Directory.GetCurrentDirectory(),
        "tests",
        "RpaFlow.PlaywrightChecks",
        "Fixtures",
        "adaptive",
        "product.golden.json");
    var golden = JsonNode.Parse(File.ReadAllText(goldenPath)) as JsonObject ??
        throw new InvalidOperationException("Golden Scrapling inválido.");
    if (golden["reference"]?["version"]?.GetValue<string>() != "0.4.14" ||
        golden["reference"]?["commit"]?.GetValue<string>() !=
            "5d213a2d4764002bfc4fed33c32fe09fa8b0bf7f" ||
        golden["highestScore"]?.GetValue<double>() != 74.65 ||
        golden["tieCount"]?.GetValue<int>() != 1 ||
        golden["winners"]?[0]?["dataId"]?.GetValue<string>() != "p1")
    {
        throw new InvalidOperationException(
            "O golden não corresponde ao Scrapling fixado ou perdeu seu vencedor.");
    }

    var fingerprint = new V2.LocatorFingerprint
    {
        Id = "p1-original",
        TagName = "article",
        Text = "Produto 1 Descrição 1",
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["class"] = "product",
            ["id"] = "p1"
        },
        Ancestors =
        [
            new V2.LocatorFingerprintNode
            {
                TagName = "section",
                Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["class"] = "products"
                }
            }
        ],
        NextSiblings = [new V2.LocatorFingerprintNode { TagName = "article" }]
    };
    static AdaptiveElementSnapshot Candidate(string dataId, string text, int index) =>
        new(
            index,
            "article",
            null,
            null,
            text,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["class"] = "product new-class",
                ["data-id"] = dataId
            },
            [
                new V2.LocatorFingerprintNode
                {
                    TagName = "section",
                    Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["class"] = "products"
                    }
                }
            ],
            [],
            [new V2.LocatorFingerprintNode { TagName = "article" }],
            Visible: true,
            Enabled: true);
    var scorer = new ScraplingBaselineScorer();
    var first = scorer.Score(fingerprint, Candidate("p1", "Produto 1 Descrição 1", 0));
    var second = scorer.Score(fingerprint, Candidate("p2", "Produto 2 Descrição 2", 1));
    var sequence = new ScraplingCompatibleSequenceMatcher();
    if (first.Baseline <= second.Baseline ||
        Math.Abs(sequence.Compare("abcd", "abxd") - 0.75d) > 0.000001d ||
        Math.Abs(sequence.CompareSequence(
            new[] { "a", "b", "c" },
            new[] { "a", "x", "c" }) - 2d / 3d) > 0.000001d)
    {
        throw new InvalidOperationException(
            "O port C# não preservou os vetores-base do SequenceMatcher/ranking.");
    }
}

static async Task CheckLocatorLearningAsync()
{
    static RpaPackageDocuments Documents(V2.LearningWriteBackMode mode) =>
        new(
            new V2.FlowDefinition
            {
                Name = "Aprendizado",
                Actions =
                [
                    new V2.FlowActionDefinition
                    {
                        Id = "navigate",
                        Type = "navigate",
                        Name = "Abrir",
                        Value = JsonSerializer.SerializeToElement("about:blank")
                    }
                ]
            },
            new V2.LocatorCatalog
            {
                Locators =
                [
                    new V2.LocatorDefinition
                    {
                        Id = "submit",
                        DisplayName = "Enviar",
                        Candidates =
                        [
                            LearnedCandidate(
                                "submit.original",
                                V2.LocatorCandidateOrigin.Developer,
                                "#original",
                                V2.DeveloperLocatorRole.Original),
                            LearnedCandidate(
                                "submit.alternative",
                                V2.LocatorCandidateOrigin.Developer,
                                "#alternative",
                                V2.DeveloperLocatorRole.Alternative)
                        ]
                    }
                ]
            },
            new V2.RpaPolicyDefinition
            {
                LocatorResilience = new V2.LocatorResiliencePolicy
                {
                    Mode = V2.LocatorResilienceMode.Adaptive,
                    LearningWriteBack = mode,
                    Promotion = mode == V2.LearningWriteBackMode.Disabled
                        ? V2.LocatorPromotionMode.Disabled
                        : V2.LocatorPromotionMode.AfterSuccessfulExecution,
                    FailedPrimary = V2.FailedPrimaryBehavior.MoveToLast
                }
            });

    static V2.LocatorCandidate LearnedCandidate(
        string id,
        V2.LocatorCandidateOrigin origin,
        string selector,
        V2.DeveloperLocatorRole? role = null) =>
        new()
        {
            Id = id,
            Origin = origin,
            DeveloperRole = role,
            OriginalOrder = role is null ? null : role == V2.DeveloperLocatorRole.Original ? 0 : 1,
            LearnedAtUtc = origin == V2.LocatorCandidateOrigin.Heuristic
                ? DateTimeOffset.Parse("2026-08-17T12:00:00Z")
                : null,
            Recipe = new V2.LocatorRecipe
            {
                Target = new V2.LocatorExpression
                {
                    Strategy = V2.LocatorStrategy.Css,
                    Selector = selector
                }
            }
        };

    static LocatorLearningObservation Observation(string candidateId) =>
        new(
            "submit",
            LearnedCandidate(
                candidateId,
                V2.LocatorCandidateOrigin.Heuristic,
                "#learned"),
            new V2.LocatorFingerprint
            {
                Id = candidateId + ".fingerprint",
                TagName = "button",
                Text = "Enviar"
            },
            FailedPrimary: true);

    var memoryDocuments = Documents(V2.LearningWriteBackMode.Memory);
    var memorySnapshot = new RpaPackageSnapshot(
        "learning-memory",
        new PackageRevision("memory-r1"),
        memoryDocuments,
        new RpaPackageOrigin("test", "memory"));
    var memory = new LocatorLearningManager(memorySnapshot);
    memory.Begin("failed");
    memory.Observe("failed", Observation("submit.failed"));
    if (!memory.TryGetOverride("failed", "submit", out _) ||
        memory.TryGetOverride("other", "submit", out _))
    {
        throw new InvalidOperationException(
            "Aprendizado provisório vazou entre execuções.");
    }

    var discarded = await memory.CompleteAsync(
        "failed",
        LocatorLearningOutcome.Failed,
        CancellationToken.None);
    if (discarded.Status != LocatorLearningCompletionStatus.Discarded ||
        memory.TryGetOverride("other", "submit", out _))
    {
        throw new InvalidOperationException("Execução falha não descartou o aprendizado.");
    }

    memory.Begin("success");
    memory.Observe("success", Observation("submit.memory"));
    var confirmed = await memory.CompleteAsync(
        "success",
        LocatorLearningOutcome.Succeeded,
        CancellationToken.None);
    if (confirmed.Status != LocatorLearningCompletionStatus.ConfirmedInMemory ||
        !memory.TryGetOverride("next", "submit", out var memoryOverride) ||
        memoryOverride.Candidate.Id != "submit.memory")
    {
        throw new InvalidOperationException(
            "Modo memory não confirmou o aprendizado após sucesso.");
    }

    foreach (var outcome in new[]
             {
                 LocatorLearningOutcome.Validated,
                 LocatorLearningOutcome.Failed,
                 LocatorLearningOutcome.Retry,
                 LocatorLearningOutcome.Cancelled,
                 LocatorLearningOutcome.Unexpected
             })
    {
        var isolated = new LocatorLearningManager(new RpaPackageSnapshot(
            $"learning-{outcome}",
            new PackageRevision($"{outcome}-r1"),
            Documents(V2.LearningWriteBackMode.Memory),
            new RpaPackageOrigin("test", outcome.ToString())));
        var execution = outcome.ToString();
        isolated.Begin(execution);
        isolated.Observe(execution, Observation($"submit.{outcome}"));
        var result = await isolated.CompleteAsync(
            execution,
            outcome,
            CancellationToken.None);
        if (result.Status != LocatorLearningCompletionStatus.Discarded ||
            isolated.TryGetOverride("next", "submit", out _))
        {
            throw new InvalidOperationException(
                $"Resultado {outcome} não descartou o aprendizado provisório.");
        }
    }

    memory.Begin("parallel-a");
    memory.Begin("parallel-b");
    memory.Observe("parallel-a", Observation("submit.a"));
    memory.Observe("parallel-b", Observation("submit.b"));
    if (!memory.TryGetOverride("parallel-a", "submit", out var overrideA) ||
        !memory.TryGetOverride("parallel-b", "submit", out var overrideB) ||
        overrideA.Candidate.Id != "submit.a" || overrideB.Candidate.Id != "submit.b")
    {
        throw new InvalidOperationException(
            "Sessões de aprendizado paralelas compartilharam estado provisório.");
    }

    _ = await memory.CompleteAsync(
        "parallel-a",
        LocatorLearningOutcome.Cancelled,
        CancellationToken.None);
    _ = await memory.CompleteAsync(
        "parallel-b",
        LocatorLearningOutcome.Unexpected,
        CancellationToken.None);

    var disabled = new LocatorLearningManager(new RpaPackageSnapshot(
        "learning-disabled",
        new PackageRevision("disabled-r1"),
        Documents(V2.LearningWriteBackMode.Disabled),
        new RpaPackageOrigin("test", "disabled")));
    disabled.Begin("disabled");
    disabled.Observe("disabled", Observation("submit.disabled"));
    var disabledResult = await disabled.CompleteAsync(
        "disabled",
        LocatorLearningOutcome.Succeeded,
        CancellationToken.None);
    if (disabledResult.Status != LocatorLearningCompletionStatus.NoChanges ||
        disabled.TryGetOverride("next", "submit", out _))
    {
        throw new InvalidOperationException("Modo disabled confirmou aprendizado.");
    }

    await CheckPersistentModeAsync(
        V2.LearningWriteBackMode.Source,
        "learning-source");
    await CheckPersistentModeAsync(
        V2.LearningWriteBackMode.Overlay,
        "learning-overlay");

    async Task CheckPersistentModeAsync(
        V2.LearningWriteBackMode mode,
        string rpaId)
    {
        var store = new MemoryRpaPackageStore();
        var documents = Documents(mode);
        var initialWrite = await store.PublishAsync(
            rpaId,
            documents,
            expectedRevision: null,
            CancellationToken.None);
        var snapshot = await store.LoadAsync(
            rpaId,
            initialWrite.Revision,
            CancellationToken.None);
        var manager = new LocatorLearningManager(snapshot, store);
        manager.Begin("persist");
        manager.Observe("persist", Observation("submit.persisted"));
        var persisted = await manager.CompleteAsync(
            "persist",
            LocatorLearningOutcome.Succeeded,
            CancellationToken.None);
        var current = await store.LoadAsync(rpaId, null, CancellationToken.None);
        var candidates = current.Locators.Locators.Single().Candidates;
        if (persisted.Status != LocatorLearningCompletionStatus.Persisted ||
            candidates[0].Id != "submit.persisted" ||
            candidates[^1].Id != "submit.original")
        {
            throw new InvalidOperationException(
                $"Modo {mode} não persistiu promoção e failedPrimary corretamente.");
        }

        var stale = new LocatorLearningManager(snapshot, store);
        stale.Begin("conflict");
        stale.Observe("conflict", Observation("submit.conflict"));
        var conflict = await stale.CompleteAsync(
            "conflict",
            LocatorLearningOutcome.Succeeded,
            CancellationToken.None);
        if (conflict.Status != LocatorLearningCompletionStatus.RevisionConflict)
        {
            throw new InvalidOperationException(
                $"Modo {mode} não detectou compare-and-swap obsoleto.");
        }
    }
}

static void CheckV2LocatorArchitecture()
{
    var repositoryRoot = Directory.GetCurrentDirectory();
    var v2Directory = Path.Combine(
        repositoryRoot,
        "src",
        "RpaFlow.Playwright",
        "V2");
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "LocatorRecipeCompiler.cs"
    };
    var offenders = Directory.EnumerateFiles(v2Directory, "*.cs")
        .Where(path => !allowed.Contains(Path.GetFileName(path)))
        .Where(path => File.ReadAllText(path).Contains(".Locator(", StringComparison.Ordinal))
        .Select(Path.GetFileName)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (offenders.Length > 0)
    {
        throw new InvalidOperationException(
            "Acesso direto a Page/Frame.Locator fora do compilador V2: " +
            string.Join(", ", offenders));
    }
}

static V2.LocatorDefinition Locator(
    string id,
    V2.LocatorStrategy strategy,
    string? selector = null,
    string? role = null,
    string? name = null,
    string? text = null) =>
    new()
    {
        Id = id,
        DisplayName = id,
        Candidates =
        [
            new V2.LocatorCandidate
            {
                Id = id + "-primary",
                Origin = V2.LocatorCandidateOrigin.Developer,
                DeveloperRole = V2.DeveloperLocatorRole.Original,
                OriginalOrder = 0,
                Recipe = new V2.LocatorRecipe
                {
                    Target = new V2.LocatorExpression
                    {
                        Strategy = strategy,
                        Selector = selector,
                        Role = role,
                        Name = name,
                        Text = text,
                        Exact = true
                    }
                }
            }
        ]
    };

static V2.LocatorCandidate Candidate(string id, string selector, int order) => new()
{
    Id = id,
    Origin = V2.LocatorCandidateOrigin.Developer,
    DeveloperRole = order == 0
        ? V2.DeveloperLocatorRole.Original
        : V2.DeveloperLocatorRole.Alternative,
    OriginalOrder = order,
    Recipe = new V2.LocatorRecipe
    {
        Target = new V2.LocatorExpression
        {
            Strategy = V2.LocatorStrategy.Css,
            Selector = selector
        }
    }
};

static FlowActionDefinition Action(
    string id,
    string type,
    string name,
    string? selector = null,
    object? value = null,
    string? optionMode = null,
    string? property = null,
    string? comparison = null,
    string? readySelector = null) =>
    new()
    {
        Id = id,
        Type = type,
        Name = name,
        Selector = selector,
        Value = JsonSerializer.SerializeToElement(value),
        OptionMode = optionMode,
        Property = property,
        Comparison = comparison,
        ReadySelector = readySelector
    };

static string DataUrl(string html) =>
    "data:text/html;charset=utf-8," + Uri.EscapeDataString(html);

static async Task CheckExecutionGuardAsync(PlaywrightRuntimeOptions options)
{
    var guardedAction = new FlowActionDefinition
    {
        Id = "efeito-protegido",
        Type = "setVariable",
        Name = "Efeito protegido",
        Target = "runtime.efeitoExecutado",
        Value = JsonSerializer.SerializeToElement(true)
    };
    var guardedFlow = new FlowDefinition
    {
        SchemaVersion = 1,
        Name = "Teste do guard antes da ação",
        Actions =
        [
            new FlowActionDefinition
            {
                Id = "condicao-protegida",
                Type = "if",
                Name = "Entrar na composição protegida",
                Condition = new FlowConditionDefinition
                {
                    Type = "value",
                    LeftValue = JsonSerializer.SerializeToElement(true),
                    Operator = "equals",
                    RightValue = JsonSerializer.SerializeToElement(true)
                },
                Actions =
                [
                    new FlowActionDefinition
                    {
                        Id = "loop-protegido",
                        Type = "repeat",
                        Name = "Repetir composição protegida",
                        Times = 1,
                        Actions =
                        [
                            new FlowActionDefinition
                            {
                                Id = "subfluxo-protegido",
                                Type = "runSubflow",
                                Name = "Executar subfluxo protegido",
                                Subflow = "efeitoFinal"
                            }
                        ]
                    }
                ]
            }
        ],
        Subflows = new Dictionary<string, List<FlowActionDefinition>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["efeitoFinal"] = [guardedAction]
        }
    };
    var guard = new BlockingExecutionGuard(guardedAction.Id);
    try
    {
        await new PlaywrightFlowExecutor(
                guardedFlow,
                options,
                executionGuard: guard)
            .ExecuteAsync(
                new FlowExecutionRequest("guard-local", [], [], []),
                CancellationToken.None);
        throw new InvalidOperationException(
            "O runtime executou uma ação cujo checkpoint autoritativo falhou.");
    }
    catch (FlowExecutionException exception)
        when (exception.Failure.ActionId == guardedAction.Id)
    {
        if (!ContainsException<CheckpointException>(exception))
        {
            throw new InvalidOperationException(
                "A falha autoritativa original não foi preservada na cadeia de exceções.");
        }
    }

    var expectedCalls = new[]
    {
        "condicao-protegida",
        "loop-protegido",
        "subfluxo-protegido",
        guardedAction.Id
    };
    if (!guard.Calls.SequenceEqual(expectedCalls, StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "O guard não percorreu condição, loop, subfluxo e ação na ordem esperada.");
    }
}

static async Task CheckAfterActionCompletionAsync(PlaywrightRuntimeOptions options)
{
    var boundaryAction = new FlowActionDefinition
    {
        Id = "limite-seguro",
        Type = "setVariable",
        Name = "Registrar limite seguro",
        Target = "runtime.limiteAtingido",
        Value = JsonSerializer.SerializeToElement(true)
    };
    var flow = new FlowDefinition
    {
        SchemaVersion = 1,
        Name = "Teste do encerramento depois da ação",
        Actions =
        [
            new FlowActionDefinition
            {
                Id = "executar-validacao-segura",
                Type = "runSubflow",
                Name = "Executar subfluxo seguro",
                Subflow = "validacaoSegura"
            },
            new FlowActionDefinition
            {
                Id = "depois-do-subfluxo",
                Type = "setVariable",
                Name = "Não executar depois do subfluxo",
                Target = "runtime.depoisDoSubfluxo",
                Value = JsonSerializer.SerializeToElement(true)
            }
        ],
        Subflows = new Dictionary<string, List<FlowActionDefinition>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["validacaoSegura"] =
            [
                new FlowActionDefinition
                {
                    Id = "repetir-validacao-segura",
                    Type = "repeat",
                    Name = "Repetir validação segura",
                    Times = 2,
                    Actions =
                    [
                        boundaryAction,
                        new FlowActionDefinition
                        {
                            Id = "depois-do-limite",
                            Type = "setVariable",
                            Name = "Não executar depois do limite",
                            Target = "runtime.depoisDoLimite",
                            Value = JsonSerializer.SerializeToElement(true)
                        }
                    ]
                }
            ]
        }
    };
    var guard = new CompletingExecutionGuard(boundaryAction.Id);
    var result = await new PlaywrightFlowExecutor(
            flow,
            options,
            executionGuard: guard)
        .ExecuteAsync(
            new FlowExecutionRequest("limite-seguro-local", [], [], []),
            CancellationToken.None);

    if (result.Output["limiteAtingido"]?.GetValue<bool>() != true ||
        result.Output["depoisDoLimite"] is not null ||
        result.Output["depoisDoSubfluxo"] is not null ||
        result.ExecutedActions != 3 ||
        !guard.AfterCalls.SequenceEqual(
            [boundaryAction.Id],
            StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "O guard posterior não encerrou a execução imediatamente depois do limite seguro.");
    }
}

static async Task CheckTypeAcrossInputsCardinalityAsync(
    PlaywrightRuntimeOptions options,
    string originUrl)
{
    var cardinalityAction = new FlowActionDefinition
    {
        Id = "digitar-pin-cardinalidade",
        Type = "typeAcrossInputs",
        Name = "Validar cardinalidade do PIN",
        Selector = "#pin .pin-segment",
        Value = JsonSerializer.SerializeToElement("12345")
    };
    var cardinalityFlow = new FlowDefinition
    {
        SchemaVersion = 1,
        Name = "Teste da cardinalidade da digitação segmentada",
        Actions =
        [
            Action(
                "navegar-cardinalidade",
                "navigate",
                "Abrir formulário segmentado",
                value: originUrl),
            cardinalityAction
        ]
    };

    try
    {
        await new PlaywrightFlowExecutor(cardinalityFlow, options)
            .ExecuteAsync(
                new FlowExecutionRequest("cardinalidade-local", [], [], []),
                CancellationToken.None);
        throw new InvalidOperationException(
            "typeAcrossInputs aceitou cardinalidade diferente do valor.");
    }
    catch (FlowExecutionException exception)
        when (exception.Failure.ActionId == cardinalityAction.Id)
    {
        var cardinalityFailure = FindException<InvalidOperationException>(exception);
        if (cardinalityFailure?.Message.Contains(
                "exige 5 inputs visíveis, mas o seletor encontrou 6",
                StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException(
                "A falha de cardinalidade não preservou uma mensagem útil.");
        }
    }
}

static bool ContainsException<TException>(Exception exception)
    where TException : Exception
{
    for (Exception? current = exception; current is not null; current = current.InnerException)
    {
        if (current is TException)
        {
            return true;
        }
    }

    return false;
}

static TException? FindException<TException>(Exception exception)
    where TException : Exception
{
    for (Exception? current = exception; current is not null; current = current.InnerException)
    {
        if (current is TException typed)
        {
            return typed;
        }
    }

    return null;
}

file sealed class BlockingExecutionGuard(string blockedActionId) : IFlowActionExecutionGuard
{
    public List<string> Calls { get; } = [];

    public ValueTask BeforeActionAsync(
        FlowActionIdentity action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(action.Id);
        if (action.Id.Equals(blockedActionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckpointException(
                "O checkpoint autoritativo de teste não foi persistido.");
        }

        return ValueTask.CompletedTask;
    }
}

file sealed class CompletingExecutionGuard(string boundaryActionId)
    : IFlowActionExecutionGuard
{
    public List<string> AfterCalls { get; } = [];

    public ValueTask BeforeActionAsync(
        FlowActionIdentity action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask<FlowActionExecutionDirective> AfterActionAsync(
        FlowActionIdentity action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!action.Id.Equals(boundaryActionId, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(FlowActionExecutionDirective.Continue);
        }

        AfterCalls.Add(action.Id);
        return ValueTask.FromResult(FlowActionExecutionDirective.CompleteExecution);
    }
}

file sealed class CheckpointException(string message) : InvalidOperationException(message);

file sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

file sealed class FakeOneTimeCodeProvider(OneTimeCodeResult result)
    : IOneTimeCodeProvider
{
    public List<OneTimeCodeRequest> Requests { get; } = [];

    public Task<OneTimeCodeResult> WaitForCodeAsync(
        OneTimeCodeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        return Task.FromResult(result);
    }
}

file sealed class RecordingFlowExecutionObserver : IFlowExecutionObserver
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
