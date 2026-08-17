using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using RpaFlow.Contracts;
using RpaFlow.Playwright;
using RpaFlow.Runtime;

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

await CheckExecutionGuardAsync(options);
await CheckAfterActionCompletionAsync(options);
await CheckTypeAcrossInputsCardinalityAsync(options, originUrl);

Console.WriteLine(
    $"OK: blocos web, guards antes/depois da ação, if, repeat, forEach aninhado, " +
    $"subfluxo e cadeia de " +
    $"iframes estável entre frames auxiliares funcionaram em HTML local com " +
    $"o navegador '{options.Browser}'.");

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
        FlowActionDefinition action,
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
        FlowActionDefinition action,
        FlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask<FlowActionExecutionDirective> AfterActionAsync(
        FlowActionDefinition action,
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
