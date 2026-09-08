using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using RpaFlow.Contracts;
using RpaFlow.Playwright;
using RpaFlow.Playwright.V2;
using RpaFlow.Playwright.V2.Adaptive;
using RpaFlow.Packages;
using RpaFlow.Runtime;
using RpaFlow.Migrator;
using SpyBrowser.Playwright;
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
    Browser: Environment.GetEnvironmentVariable("RPABLOCKLY_CHECKS_BROWSER") ??
        PlaywrightBrowserSelection.DefaultValue,
    ActionTimeoutSeconds: 15,
    UploadTimeoutSeconds: 15,
    OutputDirectory: "tmp/playwright-runtime-checks",
    ConfigurationDirectory: Directory.GetCurrentDirectory(),
    StorageStatePath: storageStatePath,
    SaveStorageState: true);
CheckBrowserSelections();
await CheckCancelledBrowserLaunchAsync(options);
await CheckSpyBrowserHumanizationAsync(options);
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
CheckCtcDecode();
CheckCaptchaPixels();
CheckSliderOffset();
CheckRecaptchaAudioUrlValidation();
await CheckCaptchaServiceClientAsync();
await CheckHumanHandoffAsync();
await CheckHumanHandoffDeadlineAsync(options);
await CheckImageOcrAsync();
await CheckCaptchaDetectorAsync();
await CheckCloudflareSidecarAsync(options.Browser);
await CheckHCaptchaServiceClientAsync();
await CheckHCaptchaSolverAsync();
await CheckAutomaticCaptchaAsync(options);
await CheckCaptchaVerificationTransitionAsync(options);
CheckV2LocatorArchitecture();

Console.WriteLine(
    $"OK: blocos web, guards antes/depois da ação, if, repeat, forEach aninhado, " +
    $"subfluxo e cadeia de " +
    $"iframes estável entre frames auxiliares funcionaram em HTML local com " +
    $"o navegador '{options.Browser}'.");

static void CheckBrowserSelections()
{
    var spyBrowser = PlaywrightBrowserSelection.Resolve("SPYBROWSER");
    if (PlaywrightBrowserSelection.DefaultValue != "spybrowser" ||
        spyBrowser.Engine != "spybrowser" ||
        spyBrowser.Channel is not null ||
        BrowserLauncher.NormalizeTimeZoneIdForBrowser("Etc/UTC") != "UTC" ||
        !PlaywrightBrowserSelection.SupportedValues.All(PlaywrightBrowserSelection.IsSupported) ||
        PlaywrightBrowserSelection.Resolve("chrome-canary") !=
            new PlaywrightBrowserSelection("chromium", "chrome-canary") ||
        PlaywrightBrowserSelection.Resolve("msedge-dev") !=
            new PlaywrightBrowserSelection("chromium", "msedge-dev"))
    {
        throw new InvalidOperationException(
            "SpyBrowser não é o padrão ou uma seleção de navegador anterior foi removida.");
    }
    Console.WriteLine("OK: SpyBrowser é o padrão e as seleções anteriores permanecem disponíveis.");
}

static async Task CheckSpyBrowserHumanizationAsync(PlaywrightRuntimeOptions baseline)
{
    foreach (var humanize in new[] { true, false })
    {
        await using var session = await BrowserLauncher.LaunchAsync(baseline with
        {
            Browser = PlaywrightBrowserSelection.DefaultValue,
            SpyBrowserHumanize = humanize,
            StorageStatePath = null,
            SaveStorageState = false
        });
        IBrowserContext? observedContext = null;
        session.Browser.Context += (_, created) => observedContext = created;
        var context = await session.Browser.NewContextAsync(new BrowserNewContextOptions());
        try
        {
            var page = await context.NewPageAsync();
            var contextIsWrapped = !ReferenceEquals(context, PlaywrightHumanizer.Unwrap(context));
            var pageIsWrapped = !ReferenceEquals(page, PlaywrightHumanizer.Unwrap(page));
            if (contextIsWrapped != humanize || pageIsWrapped != humanize ||
                !ReferenceEquals(context, observedContext) ||
                !session.Browser.Contexts.Contains(context))
            {
                throw new InvalidOperationException(
                    "SpyBrowser não respeitou a configuração de humanização do runtime.");
            }

            var observedTimeZone = await page.EvaluateAsync<string>(
                "Intl.DateTimeFormat().resolvedOptions().timeZone");
            if (!string.Equals(
                    observedTimeZone,
                    BrowserLauncher.ResolveLocalTimeZoneId(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SpyBrowser alterou o timezone local para '{observedTimeZone}'.");
            }

            if (humanize)
            {
                await page.SetContentAsync("<input id='cancel-fill' disabled>");
                using var cancellation = new CancellationTokenSource(
                    TimeSpan.FromMilliseconds(100));
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await page.Locator("#cancel-fill").FillWithRuntimeAsync(
                        new string('x', 4_096),
                        baseline with { SpyBrowserHumanize = true },
                        cancellation.Token);
                    throw new InvalidOperationException(
                        "O preenchimento ignorou o cancelamento.");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    if (stopwatch.Elapsed > TimeSpan.FromSeconds(2))
                    {
                        throw new InvalidOperationException(
                            "O preenchimento demorou para observar o cancelamento.");
                    }
                }
            }
        }
        finally
        {
            await context.CloseAsync();
        }
        if (session.Browser.Contexts.Contains(context))
        {
            throw new InvalidOperationException(
                "SpyBrowser manteve um contexto encerrado na coleção pública.");
        }
    }

    Console.WriteLine("OK: SpyBrowser liga e desliga a humanização sem trocar o provider.");
}

static async Task CheckCancelledBrowserLaunchAsync(PlaywrightRuntimeOptions baseline)
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try
    {
        await BrowserLauncher.LaunchAsync(baseline, cancellation.Token);
        throw new InvalidOperationException(
            "O launcher abriu um navegador para uma execução já cancelada.");
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        // Validado abaixo junto com cancelamento durante a criação do recurso.
    }

    var pending = new TaskCompletionSource<object>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var cleaned = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    using var duringStartup = new CancellationTokenSource(
        TimeSpan.FromMilliseconds(50));
    try
    {
        await BrowserLauncher.AwaitCancellableResourceAsync(
            pending.Task,
            _ =>
            {
                cleaned.TrySetResult();
                return Task.CompletedTask;
            },
            duringStartup.Token);
        throw new InvalidOperationException(
            "O startup ignorou o cancelamento durante a criação do recurso.");
    }
    catch (OperationCanceledException) when (duringStartup.IsCancellationRequested)
    {
        pending.SetResult(new object());
        await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    Console.WriteLine("OK: startup cancelado não inicia nem vaza recursos tardios.");
}

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
            "Os handlers V2 não cobrem exatamente os 39 tipos do catálogo.");
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

static void CheckCtcDecode()
{
    long[] indices = [1, 2, 2, 0, 3, 3, 1, 0, 0, 4, 4, 4];
    var charset = OcrCharset.Get();
    var decoded = CtcDecoder.Decode(indices, charset);
    var expected = string.Concat(charset[1], charset[2], charset[3], charset[1], charset[4]);
    if (decoded != expected)
    {
        throw new InvalidOperationException(
            $"Decode CTC divergente: '{decoded}' != '{expected}'.");
    }

    Console.WriteLine("OK: decodificador CTC ignora blanks e duplicados adjacentes.");
}

static void CheckCaptchaPixels()
{
    // RGBA 2x2 uniforme -> cinza constante
    byte[] rgba = [200, 100, 50, 255, 200, 100, 50, 255, 200, 100, 50, 255, 200, 100, 50, 255];
    var gray = CaptchaPixels.ToGrayscale(rgba, 4);
    var mid = 0.299 * 200 + 0.587 * 100 + 0.114 * 50;
    if (!gray.All(value => Math.Abs(value - mid) < 0.01))
    {
        throw new InvalidOperationException("A conversão RGBA->cinza diverge da luma esperada.");
    }

    double[] uniform = [255, 255, 255, 255];
    var (width, samples) = CaptchaPixels.ResizeAndNormalize(uniform, 2, 2, 64);
    if (width < 1 || samples.Any(value => Math.Abs(value - 1f) > 0.01))
    {
        throw new InvalidOperationException(
            "O resize bilinear alterou uma imagem uniforme.");
    }

    var mask = CaptchaPixels.ToAlphaMask(rgba, 4);
    if (!mask.All(flag => flag))
    {
        throw new InvalidOperationException("A máscara de opacidade falhou para pixels opacos.");
    }

    Console.WriteLine("OK: pixels RGBA->cinza com resize bilinear preserva uniformidade.");
}

static void CheckSliderOffset()
{
    // fundo 40x12 com marca de gradiente 8x4 na posição x=7
    var width = 40;
    var height = 12;
    var background = Enumerable.Repeat(255.0, width * height).ToArray();
    for (var y = 0; y < 4; y++)
    {
        for (var x = 7; x < 15; x++)
        {
            background[y * width + x] = (x - 7) * 30;
        }
    }

    var pieceGray = Enumerable.Range(0, 8 * 4)
        .Select(i => (i % 8) * 30.0)
        .ToArray();
    var pieceMask = Enumerable.Repeat(true, 8 * 4).ToArray();
    var (bestX, score) = SliderSolver.FindBestOffset(
        background, width, height, pieceGray, pieceMask, 8, 4);
    if (bestX != 7)
    {
        throw new InvalidOperationException(
            $"O offset do slider foi {bestX}, esperado 7 (score {score:F2}).");
    }

    try
    {
        _ = SliderSolver.FindBestMatch(
            new double[1_000 * 1_000],
            1_000,
            1_000,
            new double[100 * 100],
            Enumerable.Repeat(true, 100 * 100).ToArray(),
            100,
            100);
        throw new InvalidOperationException("Slider sem limite de trabalho foi aceito.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.InvalidPayload)
    {
    }

    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try
    {
        _ = SliderSolver.FindBestMatch(
            background,
            width,
            height,
            pieceGray,
            pieceMask,
            8,
            4,
            cancellationToken: cancelled.Token);
        throw new InvalidOperationException("Slider ignorou cancelamento.");
    }
    catch (OperationCanceledException)
    {
    }

    Console.WriteLine(
        "OK: template matching do slider encontra o offset e respeita trabalho/cancelamento.");
}

static void CheckRecaptchaAudioUrlValidation()
{
    var valid = RecaptchaV2Solver.RequireOfficialAudioUri(
        "https://www.google.com/recaptcha/api2/payload?id=teste");
    if (valid.Host != "www.google.com")
    {
        throw new InvalidOperationException("A URL oficial de áudio foi alterada.");
    }

    foreach (var invalid in new[]
             {
                 "http://www.google.com/recaptcha/api2/payload",
                 "https://www.google.com.evil.example/recaptcha/api2/payload",
                 "https://127.0.0.1/recaptcha/api2/payload",
                 "https://www.google.com:8443/recaptcha/api2/payload",
                 "https://www.google.com/fora-do-recaptcha"
             })
    {
        try
        {
            _ = RecaptchaV2Solver.RequireOfficialAudioUri(invalid);
            throw new InvalidOperationException($"A URL insegura foi aceita: {invalid}");
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("não permitida", StringComparison.Ordinal))
        {
        }
    }
    if (RecaptchaV2Solver.ResolveMaximumAttempts(
            new CaptchaOptions(RecaptchaMaxAttempts: 4),
            actionMaximumAttempts: 2) != 2)
    {
        throw new InvalidOperationException(
            "captcha.maxAttempts não sobrescreveu o limite global do reCAPTCHA.");
    }
    if (V2CaptchaActionHandler.ComposeAttempts(1, 2) != 3)
    {
        throw new InvalidOperationException(
            "As tentativas locais e remotas não foram compostas no mesmo orçamento.");
    }
    try
    {
        V2CaptchaActionHandler.ValidateExpectedAnswer("abc", expectedLength: 4, attempts: 3);
        throw new InvalidOperationException(
            "A validação pós-serviço aceitou resposta com comprimento incorreto.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.LowConfidence &&
        exception.Attempts == 3)
    {
    }

    Console.WriteLine(
        "OK: reCAPTCHA restringe URLs e o orçamento preserva tentativas compostas.");
}

static async Task CheckCaptchaServiceClientAsync()
{
    const string requestId = "request-http-v2";
    const string challengeId = "challenge-http-v2";
    const string snapshotId = "snapshot-http-v2";
    try
    {
        using var expiredClient = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: "http://127.0.0.1:1"));
        await expiredClient.SolveV2Async(
            new CaptchaServiceSolveRequest(
                requestId,
                challengeId,
                snapshotId,
                "image",
                new Dictionary<string, object?> { ["imageBase64"] = "aW1hZ2U=" },
                new Dictionary<string, object?>(),
                Hint: null,
                DateTimeOffset.UtcNow.AddSeconds(-1),
                new Dictionary<string, object?> { ["maxAttempts"] = 1 }),
            CancellationToken.None);
        throw new InvalidOperationException("Prazo expirado foi enviado ao serviço.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.DeadlineExceeded &&
        exception.Attempts == 0)
    {
    }

    var errorBody = JsonSerializer.Serialize(new
    {
        contractVersion = 2,
        requestId,
        challengeId,
        snapshotId,
        status = "Failed",
        actions = Array.Empty<object>(),
        answer = (string?)null,
        solver = (string?)null,
        modelVersion = (string?)null,
        confidence = (double?)null,
        attempts = 3,
        elapsedMs = 1,
        error = new
        {
            code = CaptchaErrorCodes.Busy,
            message = "solver ocupado após consumir as tentativas",
            retryable = true
        }
    });
    var (url, server) = StartSingleResponseServer(422, errorBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: url,
            ServiceRetryAttempts: 2));
        await client.SolveV2Async(
            new CaptchaServiceSolveRequest(
                requestId,
                challengeId,
                snapshotId,
                "image",
                new Dictionary<string, object?> { ["imageBase64"] = "aW1hZ2U=" },
                new Dictionary<string, object?> { ["width"] = 1, ["height"] = 1 },
                Hint: null,
                DateTimeOffset.UtcNow.AddSeconds(10),
                new Dictionary<string, object?>
                {
                    ["maxAttempts"] = 3,
                    ["localOnly"] = true,
                    ["allowVlmFallback"] = false
                }),
            CancellationToken.None);
        throw new InvalidOperationException("Erro HTTP V2 foi tratado como sucesso.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.Busy &&
        exception.Retryable &&
        exception.Attempts == 3)
    {
    }
    finally
    {
        await server;
    }

    var successBody = JsonSerializer.Serialize(new
    {
        contractVersion = 2,
        requestId,
        challengeId,
        snapshotId,
        status = "AnswerProduced",
        actions = new[]
        {
            new
            {
                kind = "TypeText",
                targetRole = "response",
                text = "a3x9z"
            }
        },
        answer = "a3x9z",
        solver = "retry-test",
        modelVersion = "test",
        confidence = 0.9,
        attempts = 3,
        elapsedMs = 5,
        error = (object?)null
    });
    var (successUrl, successServer) = StartSingleResponseServer(200, successBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: successUrl,
            ServiceRetryAttempts: 1));
        var result = await client.SolveV2Async(
            new CaptchaServiceSolveRequest(
                requestId,
                challengeId,
                snapshotId,
                "image",
                new Dictionary<string, object?> { ["imageBase64"] = "aW1hZ2U=" },
                new Dictionary<string, object?> { ["width"] = 1, ["height"] = 1 },
                Hint: null,
                DateTimeOffset.UtcNow.AddSeconds(10),
                new Dictionary<string, object?> { ["maxAttempts"] = 3 }),
            CancellationToken.None);
        if (result.Attempts != 3)
        {
            throw new InvalidOperationException(
                "O cliente V2 não preservou a quantidade real de tentativas.");
        }
    }
    finally
    {
        await successServer;
    }

    var (mismatchUrl, mismatchServer) = StartSingleResponseServer(200, successBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: mismatchUrl,
            ServiceRetryAttempts: 2));
        await client.SolveV2Async(
            new CaptchaServiceSolveRequest(
                requestId,
                challengeId,
                snapshotId,
                "image",
                new Dictionary<string, object?> { ["imageBase64"] = "aW1hZ2U=" },
                new Dictionary<string, object?>(),
                Hint: null,
                DateTimeOffset.UtcNow.AddSeconds(10),
                new Dictionary<string, object?> { ["maxAttempts"] = 1 }),
            CancellationToken.None);
        throw new InvalidOperationException(
            "O cliente aceitou tentativas acima do orçamento solicitado.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation &&
        exception.Attempts == 3)
    {
    }
    finally
    {
        await mismatchServer;
    }

    var missingAttemptsBody = JsonSerializer.Serialize(new
    {
        contractVersion = 2,
        requestId,
        challengeId,
        snapshotId,
        status = "AnswerProduced",
        actions = new[]
        {
            new { kind = "TypeText", targetRole = "response", text = "a3x9z" }
        },
        answer = "a3x9z",
        error = (object?)null
    });
    var (missingAttemptsUrl, missingAttemptsServer) =
        StartSingleResponseServer(200, missingAttemptsBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: missingAttemptsUrl));
        await client.SolveV2Async(
            new CaptchaServiceSolveRequest(
                requestId,
                challengeId,
                snapshotId,
                "image",
                new Dictionary<string, object?> { ["imageBase64"] = "aW1hZ2U=" },
                new Dictionary<string, object?>(),
                Hint: null,
                DateTimeOffset.UtcNow.AddSeconds(10),
                new Dictionary<string, object?> { ["maxAttempts"] = 1 }),
            CancellationToken.None);
        throw new InvalidOperationException("O cliente aceitou envelope V2 sem attempts.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation &&
        exception.Attempts == 0)
    {
    }
    finally
    {
        await missingAttemptsServer;
    }

    var malformedFailureBody = JsonSerializer.Serialize(new
    {
        contractVersion = 2,
        requestId,
        challengeId,
        snapshotId,
        status = "Failed",
        attempts = 3,
        error = (object?)null
    });
    var (malformedFailureUrl, malformedFailureServer) =
        StartSingleResponseServer(422, malformedFailureBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: malformedFailureUrl));
        await client.SolveV2Async(
            new CaptchaServiceSolveRequest(
                requestId,
                challengeId,
                snapshotId,
                "image",
                new Dictionary<string, object?> { ["imageBase64"] = "aW1hZ2U=" },
                new Dictionary<string, object?>(),
                Hint: null,
                DateTimeOffset.UtcNow.AddSeconds(10),
                new Dictionary<string, object?> { ["maxAttempts"] = 3 }),
            CancellationToken.None);
        throw new InvalidOperationException("O cliente aceitou erro V2 sem objeto error.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation &&
        exception.Attempts == 3)
    {
    }
    finally
    {
        await malformedFailureServer;
    }

    var legacyFailureBody = JsonSerializer.Serialize(new
    {
        error = new
        {
            code = CaptchaErrorCodes.Unauthorized,
            message = "token inválido",
            retryable = false
        }
    });
    var (legacyFailureUrl, legacyFailureServer) =
        StartSingleResponseServer(401, legacyFailureBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: legacyFailureUrl));
        await client.SolveV2Async(
            new CaptchaServiceSolveRequest(
                requestId,
                challengeId,
                snapshotId,
                "image",
                new Dictionary<string, object?> { ["imageBase64"] = "aW1hZ2U=" },
                new Dictionary<string, object?>(),
                Hint: null,
                DateTimeOffset.UtcNow.AddSeconds(10),
                new Dictionary<string, object?> { ["maxAttempts"] = 1 }),
            CancellationToken.None);
        throw new InvalidOperationException(
            "O cliente V2 aceitou um erro sem envelope V2 correlacionado.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation &&
        exception.Attempts == 0)
    {
    }
    finally
    {
        await legacyFailureServer;
    }

    foreach (var invalidUrl in new[]
             {
                 "http://solver.example/",
                 "https://solver.example/?destino=outro",
                 "https://usuario:senha@solver.example/"
             })
    {
        try
        {
            using var client = new CaptchaServiceClient(
                new CaptchaOptions(ServiceUrl: invalidUrl));
            throw new InvalidOperationException($"ServiceUrl insegura foi aceita: {invalidUrl}");
        }
        catch (CaptchaException exception) when (
            exception.ErrorCode == CaptchaErrorCodes.InvalidPayload)
        {
        }
    }

    Console.WriteLine(
        "OK: cliente de captcha preserva erros, tentativas e restringe transporte/URL.");
}

static (string Url, Task Server) StartSingleResponseServer(int statusCode, string body)
{
    using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();

    var listener = new HttpListener();
    var url = $"http://127.0.0.1:{port}/";
    listener.Prefixes.Add(url);
    listener.Start();
    var server = Task.Run(async () =>
    {
        try
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
        finally
        {
            listener.Close();
        }
    });
    return (url, server);
}

static async Task CheckCloudflareSidecarAsync(string browserName)
{
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
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    const string targetUrl = "https://portal.example.com/protegido";
    await page.RouteAsync("https://portal.example.com/**", async route =>
    {
        var headers = await route.Request.AllHeadersAsync();
        var cookieHeader = headers.FirstOrDefault(item =>
            item.Key.Equals("cookie", StringComparison.OrdinalIgnoreCase)).Value;
        var cleared = cookieHeader?.Contains(
            "cf_clearance=clearance-teste", StringComparison.Ordinal) == true;
        await route.FulfillAsync(new Microsoft.Playwright.RouteFulfillOptions
        {
            Status = 200,
            ContentType = "text/html; charset=utf-8",
            Body = cleared
                ? "<html><body><main id='conteudo'>acesso autorizado</main></body></html>"
                : "<html><body><div id='challenge-running'>aguarde</div></body></html>"
        });
    });
    await page.GotoAsync(targetUrl);
    var detected = (await CaptchaDetector.DetectAllAsync(
            page,
            "execucao-sidecar",
            "captcha-sidecar",
            CancellationToken.None))
        .Challenges.Single(item =>
            item.Challenge.Kind == CaptchaKind.CloudflareChallenge);
    var userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
    var sidecarBody = JsonSerializer.Serialize(new
    {
        status = "ok",
        message = "Success",
        solution = new
        {
            url = targetUrl,
            status = 200,
            cookies = new object[]
            {
                new
                {
                    name = "cookie-lateral",
                    value = "não-importar",
                    domain = ".portal.example.com",
                    path = "/"
                },
                new
                {
                    name = "cf_clearance",
                    value = "clearance-teste",
                    domain = "portal.example.com",
                    path = "/",
                    expires = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds(),
                    httpOnly = true,
                    secure = true,
                    sameSite = "None"
                }
            },
            userAgent
        },
        startTimestamp = 0,
        endTimestamp = 1,
        version = "3.0.4"
    });
    var (sidecarUrl, sidecarServer) = StartSingleResponseServer(200, sidecarBody);
    try
    {
        var result = await CloudflareSidecarAdapter.ExecuteAsync(
            page,
            detected,
            new CaptchaOptions(
                CloudflareSidecarEnabled: true,
                CloudflareSidecarProvider: "byparr",
                CloudflareSidecarUrl: sidecarUrl,
                CloudflareSidecarTimeoutSeconds: 10,
                CloudflareSidecarMaximumResponseBytes: 16 * 1024,
                CloudflareSidecarAllowedHosts: ["portal.example.com"]),
            CancellationToken.None);
        var cookies = await context.CookiesAsync();
        if (result.Status != CaptchaSolveStatus.InteractionDone ||
            await page.Locator("#conteudo").CountAsync() != 1 ||
            cookies.Count(cookie =>
                cookie.Name == "cf_clearance" &&
                cookie.Domain == "portal.example.com") != 1 ||
            cookies.Any(cookie => cookie.Name == "cookie-lateral"))
        {
            throw new InvalidOperationException(
                "O sidecar não preservou transferência filtrada e verificação na sessão original.");
        }
    }
    finally
    {
        await sidecarServer;
    }

    var maliciousBody = JsonSerializer.Serialize(new
    {
        status = "ok",
        solution = new
        {
            url = targetUrl,
            status = 200,
            cookies = new[]
            {
                new
                {
                    name = "cf_clearance",
                    value = "fora-do-escopo",
                    domain = ".evil.example",
                    path = "/",
                    secure = true
                }
            },
            userAgent
        },
        version = "3.5.0"
    });
    var (maliciousUrl, maliciousServer) = StartSingleResponseServer(200, maliciousBody);
    try
    {
        using var client = new CloudflareSidecarClient(new CaptchaOptions(
            CloudflareSidecarProvider: "flaresolverr",
            CloudflareSidecarUrl: maliciousUrl,
            CloudflareSidecarTimeoutSeconds: 10,
            CloudflareSidecarMaximumResponseBytes: 16 * 1024,
            CloudflareSidecarAllowedHosts: ["portal.example.com"]));
        await client.SolveAsync(new Uri(targetUrl), userAgent, CancellationToken.None);
        throw new InvalidOperationException("O sidecar aceitou cf_clearance de outro domínio.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation &&
        exception.Attempts == 1)
    {
    }
    finally
    {
        await maliciousServer;
    }

    var invalidStatusBody = JsonSerializer.Serialize(new
    {
        status = "ok",
        solution = new
        {
            url = targetUrl,
            status = "200",
            cookies = Array.Empty<object>(),
            userAgent
        },
        version = "3.5.0"
    });
    var (invalidStatusUrl, invalidStatusServer) = StartSingleResponseServer(200, invalidStatusBody);
    try
    {
        using var client = new CloudflareSidecarClient(new CaptchaOptions(
            CloudflareSidecarProvider: "flaresolverr",
            CloudflareSidecarUrl: invalidStatusUrl,
            CloudflareSidecarTimeoutSeconds: 10,
            CloudflareSidecarMaximumResponseBytes: 16 * 1024,
            CloudflareSidecarAllowedHosts: ["portal.example.com"]));
        await client.SolveAsync(new Uri(targetUrl), userAgent, CancellationToken.None);
        throw new InvalidOperationException("O sidecar aceitou solution.status inválido.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation &&
        exception.Attempts == 1)
    {
    }
    finally
    {
        await invalidStatusServer;
    }
    Console.WriteLine(
        "OK: sidecar Cloudflare filtra cookies e revalida no BrowserContext original.");
}

static async Task CheckHCaptchaServiceClientAsync()
{
    const string requestId = "request-hcaptcha";
    const string challengeId = "challenge-hcaptcha";
    const string snapshotId = "snapshot-hcaptcha";

    CaptchaServiceSolveRequest NewRequest() =>
        new(
            requestId,
            challengeId,
            snapshotId,
            "hcaptcha_image_label",
            new Dictionary<string, object?>
            {
                ["tilesBase64"] = new[] { "dGlsZS0w", "dGlsZS0x", "dGlsZS0y" }
            },
            new Dictionary<string, object?> { ["tileCount"] = 3 },
            "Please click each image containing an airplane",
            DateTimeOffset.UtcNow.AddSeconds(10),
            new Dictionary<string, object?> { ["maxAttempts"] = 1 },
            Provider: "hcaptcha");

    static string TilesBody(object tiles) => JsonSerializer.Serialize(new
    {
        contractVersion = 2,
        requestId = "request-hcaptcha",
        challengeId = "challenge-hcaptcha",
        snapshotId = "snapshot-hcaptcha",
        status = "AnswerProduced",
        actions = Array.Empty<object>(),
        answer = (string?)null,
        tiles,
        solver = "hcaptcha-resnet-onnx",
        modelVersion = "airplane2310@fixture",
        confidence = (double?)null,
        attempts = 1,
        elapsedMs = 5,
        error = (object?)null
    });

    var successBody = TilesBody(new[]
    {
        new { index = 0, match = false, confidence = 0.9 },
        new { index = 1, match = true, confidence = 0.8 },
        new { index = 2, match = false, confidence = 0.7 }
    });
    var (successUrl, successServer) = StartSingleResponseServer(200, successBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: successUrl));
        var result = await client.SolveV2Async(NewRequest(), CancellationToken.None);
        var decisions = result.TileDecisions;
        if (result.Status != CaptchaSolveStatus.AnswerProduced ||
            result.Kind != CaptchaKind.HCaptcha ||
            result.Attempts != 1 ||
            decisions is null ||
            decisions.Count != 3 ||
            decisions.Count(item => item.Match) != 1 ||
            !decisions[1].Match ||
            decisions[1].Confidence != 0.8 ||
            result.Actions.Count != 0)
        {
            throw new InvalidOperationException(
                "O cliente V2 não preservou as decisões de tile do hCaptcha.");
        }
    }
    finally
    {
        await successServer;
    }

    var duplicateBody = TilesBody(new[]
    {
        new { index = 1, match = true, confidence = 0.9 },
        new { index = 1, match = false, confidence = 0.8 },
        new { index = 2, match = false, confidence = 0.7 }
    });
    var (duplicateUrl, duplicateServer) = StartSingleResponseServer(200, duplicateBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: duplicateUrl));
        await client.SolveV2Async(NewRequest(), CancellationToken.None);
        throw new InvalidOperationException("Índice duplicado de tile foi aceito.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation)
    {
    }
    finally
    {
        await duplicateServer;
    }

    var outOfRangeBody = TilesBody(new[]
    {
        new { index = 0, match = false, confidence = 0.9 },
        new { index = 1, match = true, confidence = 0.8 },
        new { index = 3, match = false, confidence = 0.7 }
    });
    var (outOfRangeUrl, outOfRangeServer) = StartSingleResponseServer(200, outOfRangeBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: outOfRangeUrl));
        await client.SolveV2Async(NewRequest(), CancellationToken.None);
        throw new InvalidOperationException("Índice de tile fora da requisição foi aceito.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation)
    {
    }
    finally
    {
        await outOfRangeServer;
    }

    var mismatchBody = TilesBody(new[]
    {
        new { index = 0, match = false, confidence = 0.9 },
        new { index = 1, match = true, confidence = 0.8 }
    });
    var (mismatchUrl, mismatchServer) = StartSingleResponseServer(200, mismatchBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: mismatchUrl));
        await client.SolveV2Async(NewRequest(), CancellationToken.None);
        throw new InvalidOperationException(
            "Contagem de tiles divergente da requisição foi aceita.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation)
    {
    }
    finally
    {
        await mismatchServer;
    }

    var emptyBody = JsonSerializer.Serialize(new
    {
        contractVersion = 2,
        requestId,
        challengeId,
        snapshotId,
        status = "AnswerProduced",
        actions = Array.Empty<object>(),
        answer = (string?)null,
        tiles = (object?)null,
        solver = "hcaptcha-resnet-onnx",
        modelVersion = "airplane2310@fixture",
        confidence = (double?)null,
        attempts = 1,
        elapsedMs = 5,
        error = (object?)null
    });
    var (emptyUrl, emptyServer) = StartSingleResponseServer(200, emptyBody);
    try
    {
        using var client = new CaptchaServiceClient(new CaptchaOptions(
            ServiceUrl: emptyUrl));
        await client.SolveV2Async(NewRequest(), CancellationToken.None);
        throw new InvalidOperationException(
            "Resposta sem ações e sem tiles foi aceita.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation)
    {
    }
    finally
    {
        await emptyServer;
    }

    Console.WriteLine(
        "OK: cliente de captcha valida decisões de tile e preserva o contrato hCaptcha.");
}

static async Task CheckHCaptchaSolverAsync()
{
    using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(
        new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });

    const string anchorSrc =
        "https://newassets.hcaptcha.com/captcha/v1/fixture/static/hcaptcha.html#frame=checkbox&id=fx0";
    const string challengeSrc =
        "https://newassets.hcaptcha.com/captcha/v1/fixture/static/hcaptcha-challenge.html#frame=challenge&id=fx1";
    const string areaSrc =
        "https://newassets.hcaptcha.com/captcha/v1/fixture/static/hcaptcha-area.html#frame=challenge&id=fx2";
    const string anchorHtml =
        """
        <body>
          <div id="checkbox" aria-checked="false" role="checkbox"
               style="width:28px;height:28px;border:1px solid #333"></div>
          <script>
            document.getElementById('checkbox').addEventListener('click', () => {
              window.parent.postMessage('open-challenge', '*');
            });
            window.addEventListener('message', event => {
              if (event.data === 'mark-solved') {
                document.getElementById('checkbox').setAttribute('aria-checked', 'true');
              }
            });
          </script>
        </body>
        """;
    const string challengeHtml =
        """
        <body>
          <div class="challenge-view">
            <div class="prompt-text">Please click each image containing an airplane</div>
            <div class="task-grid">
              <div class="image" style="width:120px;height:120px;background:#a00"></div>
              <div class="image" style="width:120px;height:120px;background:#0a0"></div>
              <div class="image" style="width:120px;height:120px;background:#00a"></div>
              <div class="image" style="width:120px;height:120px;background:#aa0"></div>
              <div class="image" style="width:120px;height:120px;background:#0aa"></div>
              <div class="image" style="width:120px;height:120px;background:#a0a"></div>
              <div class="image" style="width:120px;height:120px;background:#888"></div>
              <div class="image" style="width:120px;height:120px;background:#444"></div>
              <div class="image" style="width:120px;height:120px;background:#ccc"></div>
            </div>
            <button class="button-submit">Verify</button>
          </div>
          <script>
            window.clicked = [];
            document.querySelectorAll('.task-grid .image').forEach((element, index) => {
              element.addEventListener('click', () => {
                window.clicked.push(index);
                element.style.outline = '2px solid #0f0';
              });
            });
            document.querySelector('.button-submit').addEventListener('click', () => {
              window.parent.postMessage('challenge-solved', '*');
            });
          </script>
        </body>
        """;
    const string areaHtml =
        """
        <body>
          <div class="challenge-view">
            <div class="prompt-text">Please click on the head of the animal</div>
            <div class="task-image" style="width:300px;height:300px;background:#567"></div>
            <button class="button-submit">Submit</button>
          </div>
        </body>
        """;

    static string MainHtml(string anchor, string challenge) =>
        "<body>" +
        $"<iframe id='anchor' src='{anchor}' style='width:300px;height:80px'></iframe>" +
        "<textarea name='h-captcha-response'></textarea>" +
        "<script>" +
        "window.addEventListener('message', event => {" +
        "  if (event.data === 'open-challenge') {" +
        "    const frame = document.createElement('iframe');" +
        "    frame.id = 'challenge';" +
        $"    frame.src = '{challenge}';" +
        "    frame.style = 'width:400px;height:600px';" +
        "    document.body.appendChild(frame);" +
        "  }" +
        "  if (event.data === 'challenge-solved') {" +
        "    document.getElementById('anchor').contentWindow.postMessage('mark-solved', '*');" +
        "  }" +
        "});" +
        "</script>" +
        "</body>";

    static async Task RouteFixtureAsync(
        Microsoft.Playwright.IPage page,
        string mainUrl,
        string anchorHtml,
        string challengeHtml,
        string challengeSrc,
        string anchorSrc)
    {
        await page.RouteAsync("https://newassets.hcaptcha.com/**", async route =>
        {
            var url = route.Request.Url;
            var body = url.Contains("/hcaptcha-challenge.html") ||
                url.Contains("/hcaptcha-area.html")
                ? challengeHtml
                : anchorHtml;
            await route.FulfillAsync(new Microsoft.Playwright.RouteFulfillOptions
            {
                Status = 200,
                ContentType = "text/html; charset=utf-8",
                Body = body
            });
        });
        await page.RouteAsync(mainUrl, async route =>
        {
            await route.FulfillAsync(new Microsoft.Playwright.RouteFulfillOptions
            {
                Status = 200,
                ContentType = "text/html; charset=utf-8",
                Body = MainHtml(anchorSrc, challengeSrc)
            });
        });
    }

    var (serviceUrl, serviceServer) = StartHCaptchaServiceStub([1, 4]);
    var page = await browser.NewPageAsync();
    await RouteFixtureAsync(
        page,
        "https://site.exemplo/form",
        anchorHtml,
        challengeHtml,
        challengeSrc,
        anchorSrc);
    await page.GotoAsync("https://site.exemplo/form");

    var attemptsObserved = 0;
    var outcome = await HCaptchaSolver.ExecuteAsync(
        page,
        new CaptchaOptions(
            ServiceUrl: serviceUrl,
            ServiceTimeoutSeconds: 15,
            DeadlineSeconds: 30,
            HCaptchaMaxAttempts: 2),
        maximumAttempts: null,
        value => attemptsObserved = Math.Max(attemptsObserved, value),
        CancellationToken.None);
    await serviceServer;
    if (!outcome.Solved || outcome.Unsupported || outcome.Attempts != 1 ||
        attemptsObserved != 1)
    {
        throw new InvalidOperationException(
            "O solver de hCaptcha não concluiu a grade binária na primeira rodada.");
    }
    var clicked = await page
        .FrameLocator("iframe[src*='challenge']")
        .Locator("body")
        .EvaluateAsync<string>("() => window.clicked.join(',')");
    if (clicked != "1,4")
    {
        throw new InvalidOperationException(
            $"O solver clicou os tiles '{clicked}', esperado '1,4'.");
    }
    var checkedCount = await page
        .FrameLocator("iframe[src*='checkbox']")
        .Locator("#checkbox[aria-checked='true']")
        .CountAsync();
    if (checkedCount != 1)
    {
        throw new InvalidOperationException(
            "O checkbox do hCaptcha não transitou para o estado resolvido.");
    }

    var areaPage = await browser.NewPageAsync();
    await RouteFixtureAsync(
        areaPage,
        "https://area.exemplo/form",
        anchorHtml,
        areaHtml,
        areaSrc,
        anchorSrc);
    await areaPage.GotoAsync("https://area.exemplo/form");
    var areaOutcome = await HCaptchaSolver.ExecuteAsync(
        areaPage,
        new CaptchaOptions(
            ServiceUrl: "http://127.0.0.1:1",
            ServiceTimeoutSeconds: 5,
            DeadlineSeconds: 20),
        maximumAttempts: null,
        _ => { },
        CancellationToken.None);
    if (!areaOutcome.Unsupported || areaOutcome.Solved || areaOutcome.Attempts != 0)
    {
        throw new InvalidOperationException(
            "Superfície não-binária do hCaptcha não sinalizou Unsupported para o fallback.");
    }

    Console.WriteLine(
        "OK: solver hCaptcha clica tiles decididos pelo serviço e sinaliza fallback " +
        "em superfície não-binária.");
}

static (string Url, Task Server) StartHCaptchaServiceStub(IReadOnlyCollection<int> matches)
{
    using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();

    var listener = new HttpListener();
    var url = $"http://127.0.0.1:{port}/";
    listener.Prefixes.Add(url);
    listener.Start();
    var server = Task.Run(async () =>
    {
        try
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(context.Request.InputStream);
            using var request = JsonDocument.Parse(await reader.ReadToEndAsync());
            var root = request.RootElement;
            var sent = root.GetProperty("assets")
                .GetProperty("tilesBase64").GetArrayLength();
            var tiles = string.Join(",", Enumerable.Range(0, sent).Select(index =>
                $"{{\"index\":{index}," +
                $"\"match\":{(matches.Contains(index) ? "true" : "false")}," +
                "\"confidence\":0.9}"));
            var body =
                "{\"contractVersion\":2," +
                $"\"requestId\":\"{root.GetProperty("requestId").GetString()}\"," +
                $"\"challengeId\":\"{root.GetProperty("challengeId").GetString()}\"," +
                $"\"snapshotId\":\"{root.GetProperty("snapshotId").GetString()}\"," +
                "\"status\":\"AnswerProduced\",\"actions\":[],\"answer\":null," +
                $"\"tiles\":[{tiles}]," +
                "\"solver\":\"hcaptcha-resnet-onnx\",\"modelVersion\":\"fixture@1\"," +
                "\"confidence\":null,\"attempts\":1,\"elapsedMs\":3,\"error\":null}";
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
        finally
        {
            listener.Close();
        }
    });
    return (url, server);
}

static async Task CheckHumanHandoffAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"handoff-{Guid.NewGuid():N}");
    var firstContext = new HumanHandoffContext(
        "execucao-a", "acao", "desafio", "generic", CaptchaKind.ImageText);
    var secondContext = new HumanHandoffContext(
        "execucao-b", "acao", "desafio", "generic", CaptchaKind.ImageText);
    var firstTask = HumanHandoff.ExecuteAsync(
        firstContext,
        "primeiro caso",
        directory,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMilliseconds(50),
        [],
        CancellationToken.None);
    var secondTask = HumanHandoff.ExecuteAsync(
        secondContext,
        "segundo caso",
        directory,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMilliseconds(50),
        [],
        CancellationToken.None);

    var firstFolder = HumanHandoffFolder(directory, firstContext);
    var secondFolder = HumanHandoffFolder(directory, secondContext);
    await WaitForFileAsync(Path.Combine(firstFolder, HumanHandoff.RequestFileName));
    await WaitForFileAsync(Path.Combine(secondFolder, HumanHandoff.RequestFileName));
    var firstRequest = JsonNode.Parse(
        await File.ReadAllTextAsync(Path.Combine(firstFolder, HumanHandoff.RequestFileName)))!.AsObject();
    var secondRequest = JsonNode.Parse(
        await File.ReadAllTextAsync(Path.Combine(secondFolder, HumanHandoff.RequestFileName)))!.AsObject();

    await WriteHumanHandoffResponseAsync(firstFolder, secondRequest);
    await Task.Delay(150);
    if (firstTask.IsCompleted)
    {
        throw new InvalidOperationException(
            "Uma resposta de outra execução retomou o primeiro handoff.");
    }

    await WriteHumanHandoffResponseAsync(firstFolder, firstRequest);
    await WriteHumanHandoffResponseAsync(secondFolder, secondRequest);
    await Task.WhenAll(firstTask, secondTask);
    var acknowledged = JsonNode.Parse(
        await File.ReadAllTextAsync(Path.Combine(firstFolder, HumanHandoff.RequestFileName)))!.AsObject();
    if (acknowledged["state"]?.GetValue<string>() != "Acknowledged")
    {
        throw new InvalidOperationException("O handoff confirmado não foi marcado como reconhecido.");
    }

    Console.WriteLine("OK: handoff humano correlaciona execução, ação, desafio e tentativa.");
}

static async Task CheckHumanHandoffDeadlineAsync(PlaywrightRuntimeOptions options)
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"handoff-deadline-{Guid.NewGuid():N}");
    const string executionId = "handoff-deadline";
    const string actionId = "aguardar-operador";
    var documents = new RpaPackageDocuments(
        new V2.FlowDefinition
        {
            Name = "Handoff além do deadline técnico",
            Actions =
            [
                new V2.FlowActionDefinition
                {
                    Id = actionId,
                    Type = "waitHumanInput",
                    Name = "Aguardar operador",
                    Captcha = new V2.FlowCaptchaOptionsDefinition
                    {
                        ResultOutput = "runtime.handoff"
                    }
                }
            ]
        },
        new V2.LocatorCatalog(),
        new V2.RpaPolicyDefinition());
    var snapshot = new RpaPackageSnapshot(
        "handoff-deadline",
        new PackageRevision("handoff-deadline-r1"),
        documents,
        new RpaPackageOrigin("test", "memory"));
    var execution = new PlaywrightV2FlowExecutor(
            snapshot,
            options with
            {
                OutputDirectory = directory,
                StorageStatePath = null,
                SaveStorageState = false,
                Captcha = new CaptchaOptions(
                    DeadlineSeconds: 1,
                    HumanHandoffTimeoutSeconds: 10,
                    HumanHandoffPollSeconds: 1)
            })
        .ExecuteAsync(
            new FlowExecutionRequest(executionId, [], [], []),
            CancellationToken.None);
    var context = new HumanHandoffContext(
        executionId,
        actionId,
        $"manual-{actionId}",
        null,
        CaptchaKind.Unknown);
    var folder = HumanHandoffFolder(directory, context);
    await WaitForFileAsync(Path.Combine(folder, HumanHandoff.RequestFileName));
    var request = JsonNode.Parse(
        await File.ReadAllTextAsync(Path.Combine(folder, HumanHandoff.RequestFileName)))!.AsObject();
    await Task.Delay(TimeSpan.FromMilliseconds(1_200));
    if (execution.IsCompleted)
    {
        throw new InvalidOperationException(
            "O deadline técnico encerrou indevidamente o handoff humano.");
    }

    await WriteHumanHandoffResponseAsync(folder, request);
    var result = await execution;
    if (result.Output["handoff"]?["status"]?.GetValue<string>() !=
        nameof(CaptchaSolveStatus.InteractionDone))
    {
        throw new InvalidOperationException(
            "O handoff não concluiu depois da confirmação do operador.");
    }
    Console.WriteLine(
        "OK: handoff humano usa prazo próprio além do deadline técnico de captcha.");
}

static string HumanHandoffFolder(string root, HumanHandoffContext context) =>
    Path.Combine(
        root,
        HumanHandoff.FolderName,
        context.ExecutionId,
        context.ActionId,
        context.ChallengeId);

static async Task WaitForFileAsync(string path)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
    while (!File.Exists(path) && DateTimeOffset.UtcNow < deadline)
    {
        await Task.Delay(20);
    }
    if (!File.Exists(path))
    {
        throw new InvalidOperationException($"Arquivo de handoff não criado: {path}");
    }
}

static Task WriteHumanHandoffResponseAsync(string folder, JsonObject request) =>
    File.WriteAllTextAsync(
        Path.Combine(folder, HumanHandoff.ResponseFileName),
        new JsonObject
        {
            ["contractVersion"] = request["contractVersion"]?.DeepClone(),
            ["requestId"] = request["requestId"]?.DeepClone(),
            ["executionId"] = request["executionId"]?.DeepClone(),
            ["actionId"] = request["actionId"]?.DeepClone(),
            ["challengeId"] = request["challengeId"]?.DeepClone(),
            ["action"] = "continue",
            ["respondedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        }.ToJsonString());

static async Task CheckImageOcrAsync()
{
    var modelPath = ResolveModelPath();
    if (modelPath is null)
    {
        Console.WriteLine("SKIP: modelo OCR (captcha-models/common.onnx) não localizado.");
        return;
    }

    using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(
        new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });
    var page = await browser.NewPageAsync();
    await page.GotoAsync(DataUrl(
        $"<body><img id='captcha' src='data:image/png;base64,{EmbeddedImageBase64()}'></body>"));
    await page.WaitForFunctionAsync(
        "() => document.getElementById('captcha').naturalWidth > 0",
        new Microsoft.Playwright.PageWaitForFunctionOptions { Timeout = 5_000 });

    var pixels = await PagePixelsExtractor.TryExtractAsync(
        page.Locator("#captcha"),
        CancellationToken.None);
    if (pixels is null)
    {
        throw new InvalidOperationException("Pixels do captcha de teste não extraídos.");
    }

    using var engine = new ImageOcrEngine(modelPath);
    var text = engine.Recognize(pixels.Rgba, pixels.Width, pixels.Height);
    if (!Equals(text, "a3x9z"))
    {
        throw new InvalidOperationException(
            $"O OCR embutido devolveu '{text}', esperado 'a3x9z'.");
    }

    Console.WriteLine($"OK: OCR embutido decodificou '{text}' (modelo local).");
}

static async Task CheckCaptchaDetectorAsync()
{
    using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(
        new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });
    var page = await browser.NewPageAsync();

    await page.GotoAsync(DataUrl("<body><p>sem captcha</p></body>"));
    var absent = await CaptchaDetector.DetectAllAsync(
        page, "exec-detector", "acao-detector", CancellationToken.None);
    if (absent.Status != CaptchaDetector.DetectionStatus.NotPresent ||
        await CaptchaDetector.DetectAsync(page, CancellationToken.None) != CaptchaDetector.Kind.None)
    {
        throw new InvalidOperationException("O detector não separou ausência de tipo desconhecido.");
    }

    await page.GotoAsync(DataUrl(
        "<head><title>Just a moment</title></head><body><p>conteúdo normal</p></body>"));
    if (await CaptchaDetector.DetectAsync(page, CancellationToken.None) !=
        CaptchaDetector.Kind.None)
    {
        throw new InvalidOperationException(
            "Título genérico foi aceito isoladamente como Cloudflare Challenge.");
    }

    await page.GotoAsync(DataUrl("<body><iframe id='fake'></iframe></body>"));
    await page.Locator("#fake").EvaluateAsync(
        "element => element.setAttribute('src', 'https://recaptcha.example/recaptcha/api2/anchor')");
    if (await CaptchaDetector.DetectAsync(page, CancellationToken.None) != CaptchaDetector.Kind.None)
    {
        throw new InvalidOperationException("Host não oficial foi aceito como reCAPTCHA.");
    }

    await page.GotoAsync(DataUrl("<body><iframe id='official'></iframe></body>"));
    await page.Locator("#official").EvaluateAsync(
        "element => element.setAttribute('src', 'https://www.google.com/recaptcha/api2/anchor?k=test')");
    var recaptcha = await CaptchaDetector.DetectAsync(page, CancellationToken.None);
    if (recaptcha != CaptchaDetector.Kind.RecaptchaV2)
    {
        throw new InvalidOperationException("reCAPTCHA não detectado pelo iframe.");
    }

    await page.GotoAsync(DataUrl(
        "<body><div class='g-recaptcha' data-size='invisible' " +
        "style='width:1px;height:1px'></div></body>"));
    var invisible = await CaptchaDetector.DetectAllAsync(
        page, "exec-detector", "acao-detector", CancellationToken.None);
    if (!invisible.Challenges.Any(item =>
            item.Challenge.Kind == CaptchaKind.RecaptchaV2 &&
            item.Challenge.Variant == "v2-invisible"))
    {
        throw new InvalidOperationException("reCAPTCHA v2 invisível foi confundido com v3.");
    }

    await page.GotoAsync(DataUrl(
        $"<body><img id='captcha' src='data:image/png;base64,{EmbeddedImageBase64()}'>" +
        "<input name='captcha-response'></body>"));
    var image = await CaptchaDetector.DetectAsync(page, CancellationToken.None);
    if (image != CaptchaDetector.Kind.ImageText)
    {
        throw new InvalidOperationException("Captcha de imagem não detectado por heurística.");
    }

    await page.GotoAsync(DataUrl(
        $"<body><img class='captcha' src='data:image/png;base64,{EmbeddedImageBase64()}'>" +
        $"<img class='captcha' src='data:image/png;base64,{EmbeddedImageBase64()}'>" +
        "<input name='captcha-one'><input name='captcha-two'></body>"));
    var ambiguousImages = await CaptchaDetector.DetectAllAsync(
        page, "exec-detector", "acao-detector", CancellationToken.None);
    if (ambiguousImages.Status != CaptchaDetector.DetectionStatus.Uncertain ||
        !ambiguousImages.Challenges.Any(item => item.Challenge.Kind == CaptchaKind.Unknown))
    {
        throw new InvalidOperationException(
            "Múltiplas imagens/campos foram tratados como ausência de captcha.");
    }

    await page.GotoAsync(DataUrl(
        "<body><iframe id='one' style='display:block;width:300px;height:80px'></iframe>" +
        "<iframe id='two' style='display:block;width:300px;height:80px'></iframe></body>"));
    await page.Locator("#one").EvaluateAsync(
        "element => element.setAttribute('src', 'https://www.google.com/recaptcha/api2/anchor?k=one')");
    await page.Locator("#two").EvaluateAsync(
        "element => element.setAttribute('src', 'https://www.recaptcha.net/recaptcha/api2/anchor?k=two')");
    var multiple = await CaptchaDetector.DetectAllAsync(
        page, "exec-detector", "acao-detector", CancellationToken.None);
    if (multiple.Challenges.Count(item =>
            item.Challenge.Kind == CaptchaKind.RecaptchaV2 &&
            item.Challenge.Visible) != 2)
    {
        throw new InvalidOperationException("O detector colapsou widgets reCAPTCHA distintos.");
    }

    await page.GotoAsync(DataUrl("<body><iframe id='enterprise'></iframe></body>"));
    await page.Locator("#enterprise").EvaluateAsync(
        "element => element.setAttribute('src', " +
        "'https://www.google.com/recaptcha/enterprise/anchor?k=test')");
    if (await CaptchaDetector.DetectAsync(page, CancellationToken.None) !=
        CaptchaDetector.Kind.RecaptchaEnterprise)
    {
        throw new InvalidOperationException("reCAPTCHA Enterprise não foi separado do v2.");
    }

    await page.GotoAsync(DataUrl("<body><iframe id='arkose'></iframe></body>"));
    await page.Locator("#arkose").EvaluateAsync(
        "element => element.setAttribute('src', " +
        "'https://client-api.arkoselabs.com/fc/gc/?token=test')");
    if (await CaptchaDetector.DetectAsync(page, CancellationToken.None) !=
        CaptchaDetector.Kind.ArkoseFunCaptcha)
    {
        throw new InvalidOperationException("Arkose/FunCaptcha não foi detectado.");
    }

    await page.GotoAsync(DataUrl(
        "<body><div class='geetest_holder' style='width:300px;height:120px'></div></body>"));
    if (await CaptchaDetector.DetectAsync(page, CancellationToken.None) !=
        CaptchaDetector.Kind.GeeTest)
    {
        throw new InvalidOperationException("GeeTest não foi detectado pelo container.");
    }

    await page.GotoAsync(DataUrl(
        "<body><div id='aws-waf-captcha' style='width:300px;height:120px'></div></body>"));
    if (await CaptchaDetector.DetectAsync(page, CancellationToken.None) !=
        CaptchaDetector.Kind.AwsWaf)
    {
        throw new InvalidOperationException("AWS WAF CAPTCHA não foi detectado.");
    }

    await page.GotoAsync(DataUrl(
        "<body><div class='frc-captcha' style='width:300px;height:80px'></div></body>"));
    if (await CaptchaDetector.DetectAsync(page, CancellationToken.None) !=
        CaptchaDetector.Kind.FriendlyCaptcha)
    {
        throw new InvalidOperationException("Friendly Captcha não foi detectado.");
    }

    await page.GotoAsync(DataUrl(
        "<body><div class='cf-turnstile' style='width:300px;height:80px'>" +
        "<iframe id='turnstile' style='width:300px;height:80px'></iframe>" +
        "<input name='cf-turnstile-response' value='token-cliente'></div></body>"));
    await page.Locator("#turnstile").EvaluateAsync(
        "element => element.setAttribute('src', " +
        "'https://challenges.cloudflare.com/cdn-cgi/challenge-platform/h/g/turnstile/if/ov2/av0')");
    var turnstile = await CaptchaDetector.DetectAllAsync(
        page, "exec-detector", "acao-detector", CancellationToken.None);
    var turnstileChallenges = turnstile.Challenges
        .Where(item => item.Challenge.Kind == CaptchaKind.CloudflareTurnstile)
        .ToArray();
    if (turnstileChallenges.Length != 1 || turnstileChallenges[0].Challenge.AlreadySolved)
    {
        throw new InvalidOperationException(
            "Turnstile foi duplicado ou token cliente foi tratado como aceite da aplicação.");
    }
    var passiveTurnstile = await SamePageCaptchaAdapter.ExecuteAsync(
        page,
        turnstileChallenges[0],
        allowInteractiveClick: false,
        TimeSpan.FromSeconds(1),
        CancellationToken.None);
    if (passiveTurnstile.Status != CaptchaSolveStatus.InteractionDone ||
        passiveTurnstile.Actions.Count != 0)
    {
        throw new InvalidOperationException("Turnstile passivo não observou o token cliente.");
    }

    await page.GotoAsync(DataUrl(
        "<body><div class='cf-turnstile' id='resolved' style='width:300px;height:80px'>" +
        "<input name='cf-turnstile-response' value='token-anterior'></div>" +
        "<div class='cf-turnstile' id='pending' style='width:300px;height:80px'>" +
        "<input name='cf-turnstile-response'></div></body>"));
    var scopedTurnstiles = (await CaptchaDetector.DetectAllAsync(
        page, "exec-detector", "acao-detector", CancellationToken.None)).Challenges
        .Where(item => item.Challenge.Kind == CaptchaKind.CloudflareTurnstile)
        .ToArray();
    var pendingTurnstile = scopedTurnstiles.Single(item =>
        item.Challenge.Evidence.Contains("[1]", StringComparison.Ordinal));
    var scopedResult = await SamePageCaptchaAdapter.ExecuteAsync(
        page,
        pendingTurnstile,
        allowInteractiveClick: false,
        TimeSpan.FromMilliseconds(300),
        CancellationToken.None);
    if (scopedResult.Status != CaptchaSolveStatus.NeedsHuman)
    {
        throw new InvalidOperationException(
            "Token de outro widget Turnstile liberou o desafio pendente.");
    }

    await page.GotoAsync(DataUrl(
        "<body><div class='cf-turnstile' style='width:300px;height:80px' " +
        "onclick=\"window.clicks=(window.clicks||0)+1;" +
        "document.querySelector('input').value='token';\">" +
        "<input name='cf-turnstile-response'></div></body>"));
    var clickableTurnstile = (await CaptchaDetector.DetectAllAsync(
        page, "exec-detector", "acao-detector", CancellationToken.None)).Challenges.Single();
    var clickedTurnstile = await SamePageCaptchaAdapter.ExecuteAsync(
        page,
        clickableTurnstile,
        allowInteractiveClick: true,
        TimeSpan.FromSeconds(2),
        CancellationToken.None);
    var clickCount = await page.EvaluateAsync<int>("() => window.clicks || 0");
    if (clickedTurnstile.Status != CaptchaSolveStatus.InteractionDone ||
        clickedTurnstile.Actions.Count != 1 || clickCount != 1)
    {
        throw new InvalidOperationException("Turnstile não limitou a interação autorizada a um clique.");
    }

    await page.GotoAsync(DataUrl(
        "<body><div class='frc-captcha' style='width:300px;height:80px'>" +
        "<input name='frc-captcha-solution'></div>" +
        "<script>setTimeout(() => document.querySelector('input').value='proof', 100)</script>" +
        "</body>"));
    var friendly = (await CaptchaDetector.DetectAllAsync(
        page, "exec-detector", "acao-detector", CancellationToken.None)).Challenges.Single();
    var friendlyResult = await SamePageCaptchaAdapter.ExecuteAsync(
        page,
        friendly,
        allowInteractiveClick: false,
        TimeSpan.FromSeconds(2),
        CancellationToken.None);
    if (friendlyResult.Status != CaptchaSolveStatus.InteractionDone)
    {
        throw new InvalidOperationException("Friendly Captcha não observou o proof same-page.");
    }

    await page.GotoAsync(DataUrl(
        "<body><div id='challenge-stage'>aguardando</div>" +
        "<script>setTimeout(() => document.querySelector('#challenge-stage').remove(), 1000)</script>" +
        "</body>"));
    var cloudflare = (await CaptchaDetector.DetectAllAsync(
        page, "exec-detector", "acao-detector", CancellationToken.None)).Challenges.Single();
    var cloudflareResult = await SamePageCaptchaAdapter.ExecuteAsync(
        page,
        cloudflare,
        allowInteractiveClick: false,
        TimeSpan.FromSeconds(2),
        CancellationToken.None);
    if (cloudflareResult.Status != CaptchaSolveStatus.InteractionDone ||
        cloudflareResult.Actions.Count != 0)
    {
        throw new InvalidOperationException(
            "Cloudflare Managed Challenge não concluiu por observação passiva.");
    }

    await page.GotoAsync(DataUrl(
        "<body><div id='surface' style='position:absolute;left:20px;top:30px;" +
         "width:200px;height:100px'></div><script>" +
         "window.clicks=[];" +
         "document.getElementById('surface').addEventListener('click', e => {" +
         "const marker=document.createElement('span');" +
         "marker.style='position:absolute;width:5px;height:5px;background:red;pointer-events:none';" +
         "marker.style.left=(e.offsetX-2)+'px';marker.style.top=(e.offsetY-2)+'px';" +
         "e.currentTarget.appendChild(marker);}, { once: true });" +
         "document.addEventListener('click', e => window.clicks.push([e.clientX,e.clientY]));" +
         "document.addEventListener('mouseup', e => window.lastUp=[e.clientX,e.clientY]);" +
         "</script></body>"));
    var surfaceBounds = await page.Locator("#surface").BoundingBoxAsync() ??
        throw new InvalidOperationException("Fixture visual sem bounding box.");
    var surfaceElement = await page.Locator("#surface").ElementHandleAsync() ??
        throw new InvalidOperationException("Fixture visual sem elemento estável.");
    var surfaceScreenshot = await page.Locator("#surface").ScreenshotAsync();
    var visualActions = await VisualCaptchaAdapter.ExecuteActionsAsync(
        page,
        [
            new CaptchaPlannedAction(CaptchaActionKind.Click, X: 25, Y: 15),
            new CaptchaPlannedAction(
                CaptchaActionKind.Drag,
                X: 10,
                Y: 10,
                ToX: 90,
                ToY: 40)
        ],
        surfaceBounds,
        imageWidth: 100,
        imageHeight: 50,
        CancellationToken.None,
        token => VisualCaptchaAdapter.ValidateRetainedElementAsync(
            surfaceElement,
            surfaceBounds,
            token),
        token => VisualCaptchaAdapter.ValidateRetainedSnapshotAsync(
            page.Locator("#surface"),
            surfaceElement,
            surfaceBounds,
            surfaceScreenshot,
            token));
    var lastClick = await page.EvaluateAsync<float[]>("() => window.clicks[0]");
    var lastUp = await page.EvaluateAsync<float[]>("() => window.lastUp");
    if (visualActions.Count != 2 ||
        Math.Abs(lastClick[0] - 70) > 1 || Math.Abs(lastClick[1] - 60) > 1 ||
        Math.Abs(lastUp[0] - 200) > 1 || Math.Abs(lastUp[1] - 110) > 1)
    {
        throw new InvalidOperationException(
            "Coordenadas VLM não foram transformadas do snapshot para pixels CSS.");
    }
    try
    {
        await VisualCaptchaAdapter.ExecuteActionsAsync(
            page,
            [new CaptchaPlannedAction(CaptchaActionKind.Click, X: 100, Y: 10)],
            surfaceBounds,
            imageWidth: 100,
            imageHeight: 50,
            CancellationToken.None);
        throw new InvalidOperationException("Coordenada fora do snapshot foi executada.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.ContractViolation)
    {
    }

    await page.EvaluateAsync(
        "() => document.getElementById('surface').addEventListener('click', " +
        "e => e.currentTarget.style.backgroundColor='rgb(255, 0, 0)', { once: true })");
    try
    {
        await VisualCaptchaAdapter.ExecuteActionsAsync(
            page,
            [
                new CaptchaPlannedAction(CaptchaActionKind.Click, X: 10, Y: 10),
                new CaptchaPlannedAction(CaptchaActionKind.Click, X: 20, Y: 20)
            ],
            surfaceBounds,
            imageWidth: 100,
            imageHeight: 50,
            CancellationToken.None,
            token => VisualCaptchaAdapter.ValidateRetainedElementAsync(
                surfaceElement,
                surfaceBounds,
                token));
        throw new InvalidOperationException("Desafio visual alterado aceitou a segunda ação.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.StaleSnapshot)
    {
    }
    await page.Locator("#surface").EvaluateAsync("element => element.style.backgroundColor = ''");

    await page.EvaluateAsync(
        "() => document.getElementById('surface').addEventListener('click', e => {" +
        "const marker=document.createElement('span');marker.className='end-mutation';" +
        "marker.style='position:absolute;left:177px;top:77px;width:7px;height:7px;background:blue';" +
        "e.currentTarget.appendChild(marker);}, { once: true })");
    try
    {
        await VisualCaptchaAdapter.ExecuteActionsAsync(
            page,
            [
                new CaptchaPlannedAction(CaptchaActionKind.Click, X: 10, Y: 10),
                new CaptchaPlannedAction(
                    CaptchaActionKind.Drag,
                    X: 10,
                    Y: 10,
                    ToX: 90,
                    ToY: 40)
            ],
            surfaceBounds,
            imageWidth: 100,
            imageHeight: 50,
            CancellationToken.None,
            token => VisualCaptchaAdapter.ValidateRetainedElementAsync(
                surfaceElement,
                surfaceBounds,
                token));
        throw new InvalidOperationException("Destino visual alterado aceitou o arrasto.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.StaleSnapshot)
    {
    }
    await page.Locator(".end-mutation").EvaluateAsync("element => element.remove()");

    await page.EvaluateAsync(
        "() => { const old = document.getElementById('surface'); " +
        "old.addEventListener('click', () => old.outerHTML = " +
        "'<div id=\"surface\" style=\"position:absolute;left:20px;top:30px;" +
        "width:200px;height:100px\"></div>', { once: true }); }");
    var replacedElement = await page.Locator("#surface").ElementHandleAsync() ??
        throw new InvalidOperationException("Fixture visual substituível não foi encontrada.");
    try
    {
        await VisualCaptchaAdapter.ExecuteActionsAsync(
            page,
            [
                new CaptchaPlannedAction(CaptchaActionKind.Click, X: 10, Y: 10),
                new CaptchaPlannedAction(CaptchaActionKind.Click, X: 20, Y: 20)
            ],
            surfaceBounds,
            imageWidth: 100,
            imageHeight: 50,
            CancellationToken.None,
            token => VisualCaptchaAdapter.ValidateRetainedElementAsync(
                replacedElement,
                surfaceBounds,
                token));
        throw new InvalidOperationException("Elemento visual substituído aceitou o segundo clique.");
    }
    catch (CaptchaException exception) when (
        exception.ErrorCode == CaptchaErrorCodes.StaleSnapshot)
    {
    }

    Console.WriteLine(
        "OK: detector, adapters same-page e executor visual preservam escopo e geometria.");
}

static async Task CheckAutomaticCaptchaAsync(PlaywrightRuntimeOptions options)
{
    var url = DataUrl(
        "<body><div class='frc-captcha' style='width:300px;height:80px'>" +
        "<input name='frc-captcha-solution' value='proof'></div></body>");
    var documents = new RpaPackageDocuments(
        new V2.FlowDefinition
        {
            Name = "Captcha automático opt-in",
            Actions =
            [
                new V2.FlowActionDefinition
                {
                    Id = "abrir-captcha",
                    Type = "navigate",
                    Name = "Abrir captcha",
                    Value = JsonSerializer.SerializeToElement(url),
                    Captcha = new V2.FlowCaptchaOptionsDefinition
                    {
                        Kind = "friendlyCaptcha",
                        ResultOutput = "runtime.autoCaptcha"
                    }
                }
            ]
        },
        new V2.LocatorCatalog(),
        new V2.RpaPolicyDefinition
        {
            LocatorResilience = new V2.LocatorResiliencePolicy
            {
                Mode = V2.LocatorResilienceMode.Strict
            }
        });
    var snapshot = new RpaPackageSnapshot(
        "captcha-auto",
        new PackageRevision("captcha-auto-r1"),
        documents,
        new RpaPackageOrigin("test", "memory"));
    var request = new FlowExecutionRequest("captcha-auto", [], [], []);
    var enabled = await new PlaywrightV2FlowExecutor(
            snapshot,
            options with
            {
                StorageStatePath = null,
                SaveStorageState = false,
                Captcha = new CaptchaOptions(
                    AutoSolveEnabled: true,
                    SamePageWaitSeconds: 2)
            })
        .ExecuteAsync(request, CancellationToken.None);
    if (enabled.Output["autoCaptcha"]?["status"]?.GetValue<string>() !=
            nameof(CaptchaSolveStatus.InteractionDone) ||
        enabled.Output["autoCaptcha"]?["kind"]?.GetValue<string>() !=
            nameof(CaptchaKind.FriendlyCaptcha))
    {
        throw new InvalidOperationException(
            "AutoSolveEnabled não encaminhou o desafio detectado ao adapter correto.");
    }

    var turnstileUrl = DataUrl(
        "<body><div class='cf-turnstile' style='width:300px;height:80px'>" +
        "<input name='cf-turnstile-response' value='proof'></div></body>");
    var turnstileAction = new V2.FlowActionDefinition
    {
        Id = "abrir-turnstile",
        Type = "navigate",
        Name = "Abrir Turnstile",
        Value = JsonSerializer.SerializeToElement(turnstileUrl),
        Captcha = new V2.FlowCaptchaOptionsDefinition
        {
            Kind = "turnstile",
            ResultOutput = "runtime.autoTurnstile"
        }
    };
    var turnstileSnapshot = new RpaPackageSnapshot(
        "captcha-auto-turnstile",
        new PackageRevision("captcha-auto-turnstile-r1"),
        new RpaPackageDocuments(
            new V2.FlowDefinition
            {
                Name = "Mapeamento canônico do Turnstile",
                Actions = [turnstileAction]
            },
            new V2.LocatorCatalog(),
            documents.Policy),
        new RpaPackageOrigin("test", "memory"));
    var turnstileResult = await new PlaywrightV2FlowExecutor(
            turnstileSnapshot,
            options with
            {
                StorageStatePath = null,
                SaveStorageState = false,
                Captcha = new CaptchaOptions(
                    AutoSolveEnabled: true,
                    SamePageWaitSeconds: 2)
            })
        .ExecuteAsync(
            request with { ExecutionId = "captcha-auto-turnstile" },
            CancellationToken.None);
    if (turnstileResult.Output["autoTurnstile"]?["kind"]?.GetValue<string>() !=
        nameof(CaptchaKind.CloudflareTurnstile))
    {
        throw new InvalidOperationException(
            "kind=turnstile validado pelo schema não foi mapeado no runtime.");
    }

    var completionGuard = new CompletingExecutionGuard(turnstileAction.Id);
    var guardedResult = await new PlaywrightV2FlowExecutor(
            turnstileSnapshot,
            options with
            {
                StorageStatePath = null,
                SaveStorageState = false,
                Captcha = new CaptchaOptions(AutoSolveEnabled: true)
            },
            executionGuard: completionGuard)
        .ExecuteAsync(
            request with { ExecutionId = "captcha-auto-checkpoint" },
            CancellationToken.None);
    if (guardedResult.Output["autoTurnstile"] is not null ||
        !completionGuard.AfterCalls.Contains(turnstileAction.Id))
    {
        throw new InvalidOperationException(
            "O auto-solve ocorreu antes do checkpoint posterior da ação original.");
    }

    var disabled = await new PlaywrightV2FlowExecutor(
            snapshot,
            options with
            {
                StorageStatePath = null,
                SaveStorageState = false,
                Captcha = new CaptchaOptions(AutoSolveEnabled: false)
            })
        .ExecuteAsync(request with { ExecutionId = "captcha-auto-disabled" }, CancellationToken.None);
    if (disabled.Output["autoCaptcha"] is not null)
    {
        throw new InvalidOperationException("AutoSolveEnabled=false iniciou detecção automática.");
    }
    Console.WriteLine("OK: detecção automática pós-navegação permanece opt-in e roteada.");
}

static async Task CheckCaptchaVerificationTransitionAsync(PlaywrightRuntimeOptions options)
{
    var url = DataUrl(
        "<body><div class='frc-captcha' style='width:300px;height:80px'>" +
        "<input name='frc-captcha-solution' value='proof'></div>" +
        "<div id='captcha-success'>marcador já visível</div></body>");
    var result = await new PlaywrightV2FlowExecutor(
            new RpaPackageSnapshot(
                "captcha-verification-transition",
                new PackageRevision("captcha-verification-transition-r1"),
                new RpaPackageDocuments(
                    new V2.FlowDefinition
                    {
                        Name = "Verificação exige transição",
                        Actions =
                        [
                            new V2.FlowActionDefinition
                            {
                                Id = "abrir-verificacao",
                                Type = "navigate",
                                Name = "Abrir verificação",
                                Value = JsonSerializer.SerializeToElement(url)
                            },
                            new V2.FlowActionDefinition
                            {
                                Id = "resolver-com-verificacao",
                                Type = "solveCaptcha",
                                Name = "Resolver com verificação",
                                Optional = true,
                                Success = new V2.LocatorUseDefinition
                                {
                                    LocatorId = "captcha-success",
                                    Cardinality = V2.LocatorCardinality.Single
                                },
                                Captcha = new V2.FlowCaptchaOptionsDefinition
                                {
                                    Kind = "friendlyCaptcha",
                                    VerificationMode = "solveAndVerify",
                                    ResultOutput = "runtime.verifiedCaptcha"
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
                                Id = "captcha-success",
                                DisplayName = "Sucesso do captcha",
                                Candidates =
                                [
                                    new V2.LocatorCandidate
                                    {
                                        Id = "captcha-success-original",
                                        Origin = V2.LocatorCandidateOrigin.Developer,
                                        DeveloperRole = V2.DeveloperLocatorRole.Original,
                                        OriginalOrder = 0,
                                        Recipe = new V2.LocatorRecipe
                                        {
                                            Target = new V2.LocatorExpression
                                            {
                                                Strategy = V2.LocatorStrategy.Css,
                                                Selector = "#captcha-success"
                                            }
                                        }
                                    }
                                ]
                            }
                        ]
                    },
                    new V2.RpaPolicyDefinition()),
                new RpaPackageOrigin("test", "memory")),
            options with
            {
                StorageStatePath = null,
                SaveStorageState = false,
                Captcha = new CaptchaOptions(SamePageWaitSeconds: 2)
            })
        .ExecuteAsync(
            new FlowExecutionRequest("captcha-verification-transition", [], [], []),
            CancellationToken.None);
    if (result.Output["verifiedCaptcha"]?["status"]?.GetValue<string>() !=
            nameof(CaptchaSolveStatus.Failed) ||
        result.Output["verifiedCaptcha"]?["errorCode"]?.GetValue<string>() !=
            CaptchaErrorCodes.VerificationFailed)
    {
        throw new InvalidOperationException(
            "Locator previamente visível foi aceito como prova de resolução do captcha.");
    }
    Console.WriteLine("OK: Solved exige transição observável da pós-condição.");
}



static string? ResolveModelPath()
{
    var candidates = new[]
    {
        Environment.GetEnvironmentVariable("RPABLOCKLY_CAPTCHA_MODEL"),
        Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "captcha-models", "common.onnx")),
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(AppContext.BaseDirectory)!,
                "..", "..", "..", "..", "..",
                "captcha-models", "common.onnx"))
    };
    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static string EmbeddedImageBase64() =>
    "iVBORw0KGgoAAAANSUhEUgAAAKAAAAA8CAYAAADha7EVAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAP4SURBVHhe7ZxBcuowEERzXQ7B1muuAFdgyZYbcApOoWAKEtvqlkeWorFJv6pZRRFC8zQjper/ryCEIxJQuCIBhSsSULgiAYUrElC4IgGFKxJQuCIBhSsSULgiAYUrElC4IgGFKxJQuCIBhStft9stlIYQS6lSAZGULUNsl49owUjKliGWoztgBZCULWPLrF7A+6ULu90uiu5yf43w5B4uXby23a4LLZeHpGwZJaxWQCbeNI5OBeB2xOsZxzH8hwaNpLTGKgW0yveOthKyqseibTXcGusT8H4JHUxkKtol2Vb5JtFdHtoKxOoEhNVvVOJu4Tj9+SOa3AnJ4Rgv7xj9vI913FnXx8oEBO0NVQ8kQoM+jA4HFAutT1UQUlfAufZpSsK4wpkTHAmIKyUTFd87h60d3f14649b9XRs7l1yGJ9zr6wj4Jx40yitVqDNQVFJO4w/3tLWkTD8lTtfLSVgTwUBSaWZCSiMBSgVFwE+GiaV2DKmhoDjgycBe4oFXPQqfEbO38hSyZpLxkx1g0KjOdEa+GfDfZGAEWUCwtYLNsecZAarskaJ6edbWu8vSCo8lqw38yFCD3fpFWZFVHqE/G442xu0meZ9TN0xjZOYK3VKEnKnHEvIDssjMgTEj6K8ObZAJQHn+TMBn2GphAkxfmKuKpe0zUcY5aHyZV1btsHfC5iQZ2kngQmyJJdUsHfYHkYWkUlY1kj363PufUMqC5hXIZYK2GO/j42hrTirtc1LeLwAkWY/g89bsldrpoqAvGWko2hTUaWwTMiqYJaAL9Bc73my18cPr60yb5NiAdOX+9+2UXQHhIBqUVBh+qiaaCBnan66j59a+l6UCYgqANkwq4DTcXz/8wVMH5Y+6t2zUFdg3+W/vHgRRQLGCeWvNIuAMBE8a3ktbuYB8hPJpMfS46qW8Udruq7Pe/EiCgTM2GTysot8gePwnEho3uJw64UPhUfQedD6kLCpu+EQsi81K/HaqSxgH5OTm6g8ccGyzYlbaV71fQuB2x8TgKxv+EWsh40cCjz2c6ncgvMCbjStCumgVQsegKFgRCrSihe9+MEXXTTPMJJXhbagf+thjbJHSI4sXReNZdJkJ4eWDFxlos8l3wOvj1VpEjVFHsZgXpTYllFCmYA9FgmfgoDEpU6x8dFAK9+DVOudUqUVT+Nwhgnr43ra49+xxv4Urq+5tky5gC8syY6TPH/ZppViUPWmyX3G+QB/73AGY59xDad9PH6Y6Ciup7Cfju8jdbBe1KyAW6aKgDA5DUNsF/3vWMKVai1YiCVIQOGKBBSuSEDhigQUrkhA4YoEFK5IQOGKBBSuSEDhigQUrkhA4YoEFK5IQOFICN8WQpe3OBxNtAAAAABJRU5ErkJggg==";

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
