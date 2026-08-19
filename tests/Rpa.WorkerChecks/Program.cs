using System.Text.RegularExpressions;
using Microsoft.Graph.Models;
using Rpa.Worker.Authentication;
using Rpa.Worker.Configuration;
using Rpa.Worker.Execution;
using Rpa.Worker.Domain;
using Rpa.Worker.Hosting;
using Rpa.Worker.Data;
using RpaFlow.Contracts;
using RpaFlow.Playwright;
using RpaFlow.Runtime;

CheckDisabledProviderDoesNotRequireCredentials();
CheckEnabledProviderConfiguration();
CheckParsingAndNewestMessage();
await CheckPollingTimeoutAndAliasLockAsync();
CheckFlowProviderFences();
CheckOneTimeCodeOutputSanitization();
CheckConfiguredExecutionGuard();
CheckWorkerFailurePolicy();
CheckWorkerReadiness();
await CheckSafeValidationBoundaryConfigurationAsync();
Console.WriteLine("Worker e provider de OTP por e-mail validados com sucesso.");

static void CheckConfiguredExecutionGuard()
{
    var request = new FlowExecutionRequest("guard-worker", [], [], []);
    var ordinaryAction = new FlowActionDefinition
    {
        Id = "preparar-evidencia",
        Type = "setVariable",
        Name = "Preparar evidência"
    };
    var boundaryAction = new FlowActionDefinition
    {
        Id = "registrar-limite-seguro",
        Type = "screenshot",
        Name = "Registrar limite seguro"
    };
    var irreversibleAction = new FlowActionDefinition
    {
        Id = "confirmar-operacao",
        Type = "click",
        Name = "Confirmar operação"
    };
    var definition = new RpaDefinitionOptions
    {
        SafeValidationBoundaryActionId = boundaryAction.Id,
        IrreversibleActionIds = [irreversibleAction.Id]
    };
    var safeGuard = new ConfiguredExecutionGuard(
        WorkerExecutionMode.SafeValidation,
        definition);

    safeGuard.BeforeActionAsync(
            ordinaryAction,
            request,
            CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    Check(
        safeGuard.AfterActionAsync(
                ordinaryAction,
                request,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult() == FlowActionExecutionDirective.Continue,
        "o guard continua depois de uma ação anterior ao limite seguro");
    safeGuard.BeforeActionAsync(
            boundaryAction,
            request,
            CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    Check(
        safeGuard.AfterActionAsync(
                boundaryAction,
                request,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult() == FlowActionExecutionDirective.CompleteExecution &&
        safeGuard.SafeValidationBoundaryReached,
        "o guard encerra com sucesso depois da ação de limite seguro");

    var productionGuard = new ConfiguredExecutionGuard(
        WorkerExecutionMode.Production,
        definition);
    Check(
        productionGuard.AfterActionAsync(
                boundaryAction,
                request,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult() == FlowActionExecutionDirective.Continue &&
        !productionGuard.SafeValidationBoundaryReached,
        "o limite seguro não interrompe uma execução de produção");

    var unsafeOrderGuard = new ConfiguredExecutionGuard(
        WorkerExecutionMode.SafeValidation,
        definition);
    AssertInvalid(
        () => unsafeOrderGuard.BeforeActionAsync(
                irreversibleAction,
                request,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult(),
        "antes do limite seguro configurado");

    var legacyDefinition = new RpaDefinitionOptions
    {
        IrreversibleActionIds = [irreversibleAction.Id]
    };
    var legacyGuard = new ConfiguredExecutionGuard(
        WorkerExecutionMode.SafeValidation,
        legacyDefinition);
    AssertInvalid(
        () => legacyGuard.BeforeActionAsync(
                irreversibleAction,
                request,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult(),
        "parou antes da ação irreversível");
}

static void CheckWorkerFailurePolicy()
{
    var item = new RpaWorkItem(
        Guid.NewGuid(), "exemplo", null, null, 1, 3, "{}", "{}", "{}");
    var definition = new RpaDefinitionOptions();
    var transient = new FlowExecutionException(
        new FlowExecutionFailure(
            "execucao", item.WorkItemId.ToString(), null,
            FlowFailureCategory.Timeout, true, "Timeout transitório."),
        new TimeoutException("Timeout transitório."));
    var retry = WorkerFailurePolicy.Decide(
        transient, item, definition, null, false, false);
    Check(retry.Retry && !retry.PreserveAttempt,
        "falha transitória agenda nova tentativa e consome a atual");

    var leadership = WorkerFailurePolicy.Decide(
        new OperationCanceledException("Trava perdida."),
        item, definition, null, false, true);
    Check(leadership.Retry && leadership.PreserveAttempt &&
          leadership.ErrorCode == "TRAVA_GLOBAL_PERDIDA",
        "perda da trava agenda retomada sem consumir tentativa do caso");

    var paths = new WorkerPaths(".", ".", ".", ".");
    var repository = new SqlWorkItemRepository(
        CreateWorkerOptions(), new WorkerEnvironment(string.Empty, paths));
    var observer = new WorkerFlowExecutionObserver(repository, ["enviar-login"], []);
    observer.Track(new FlowExecutionEvent(
        "actionStarted", "execucao", item.WorkItemId.ToString(), null,
        DateTimeOffset.UtcNow, "enviar-login", "Enviar login", "click"));
    var blocked = WorkerFailurePolicy.Decide(
        transient, item, definition, observer, false, false);
    Check(!blocked.Retry && blocked.ErrorCode == "REPETICAO_DE_LOGIN_BLOQUEADA",
        "login iniciado sem marcador bloqueia repetição automática");
    observer.Track(new FlowExecutionEvent(
        "actionCompleted", "execucao", item.WorkItemId.ToString(), null,
        DateTimeOffset.UtcNow, "concluir-login", "Concluir login",
        "completeAuthenticationAttempt"));
    var released = WorkerFailurePolicy.Decide(
        transient, item, definition, observer, false, false);
    Check(released.Retry,
        "marcador concluído libera retry técnico posterior sem liberar MFA");
    Check(FlowActionHandlerRegistry.Default.SupportedTypes.Contains(
            "completeAuthenticationAttempt"),
        "runtime possui handler para o marcador de autenticação");
}

static void CheckWorkerReadiness()
{
    var state = new WorkerRuntimeState();
    state.MarkValidationPassed(true, 1, 1);
    state.MarkLeadershipAcquired();
    state.MarkPollingStarted();
    state.MarkPollingSucceeded(DateTimeOffset.UtcNow.AddSeconds(5));
    var ready = WorkerReadinessEvaluator.Evaluate(
        state.GetSnapshot(), DateTimeOffset.UtcNow, 5);
    Check(ready.Ready && ready.AcceptingClaims,
        "readiness confirma liderança, polling recente e vaga disponível");
    state.MarkExecutionStarted();
    var busy = WorkerReadinessEvaluator.Evaluate(
        state.GetSnapshot(), DateTimeOffset.UtcNow, 5);
    Check(busy.Ready && !busy.AcceptingClaims,
        "worker ocupado permanece saudável, mas não anuncia vaga imediata");
}

static async Task CheckSafeValidationBoundaryConfigurationAsync()
{
    var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
    var options = CreateWorkerOptions();
    var definition = options.Definitions["exemplo"];
    definition.FlowFile = "examples/RpaExemplo/flow.production.json";
    definition.SafeValidationBoundaryActionId = "iniciar-fluxo";
    var paths = new WorkerPaths(
        repositoryRoot,
        repositoryRoot,
        Path.Combine(repositoryRoot, "artifacts"),
        Path.Combine(repositoryRoot, "storage", "sessions"));
    await RpaWorkerOptionsValidator.ValidateFlowsAsync(
        options,
        paths,
        CancellationToken.None);
    Pass("o worker aceita um limite seguro que referencia uma ação existente");

    definition.SafeValidationBoundaryActionId = "acao-inexistente";
    await AssertInvalidAsync(
        () => RpaWorkerOptionsValidator.ValidateFlowsAsync(
            options,
            paths,
            CancellationToken.None),
        "SafeValidationBoundaryActionId referencia a ação inexistente");

    definition.SafeValidationBoundaryActionId = "iniciar-fluxo";
    definition.IrreversibleActionIds = ["iniciar-fluxo"];
    await AssertInvalidAsync(
        () => RpaWorkerOptionsValidator.ValidateFlowsAsync(
            options,
            paths,
            CancellationToken.None),
        "não pode ser também uma ação irreversível");

    definition.IrreversibleActionIds = [];
    definition.SafeValidationBoundaryActionId = "executar-subfluxo";
    AssertInvalid(
        () => RpaWorkerOptionsValidator.ValidateSafeValidationBoundary(
            "exemplo",
            definition,
            [
                new FlowActionDefinition
                {
                    Id = "executar-subfluxo",
                    Type = "runSubflow",
                    Name = "Executar subfluxo",
                    Subflow = "validacao"
                }
            ]),
        "deve referenciar uma ação-folha");
}

static void CheckDisabledProviderDoesNotRequireCredentials()
{
    var options = CreateWorkerOptions();
    options.EmailReader.Providers["email-otp"] = new EmailOneTimeCodeProviderOptions();
    _ = RpaWorkerOptionsValidator.Validate(options, Directory.GetCurrentDirectory(), string.Empty);
    Pass("provider desabilitado não exige credenciais");
}

static void CheckEnabledProviderConfiguration()
{
    var options = CreateWorkerOptions();
    var provider = CreateEnabledProvider();
    options.EmailReader.Providers["email-otp"] = provider;

    AssertInvalid(
        () => RpaWorkerOptionsValidator.Validate(
            options,
            Directory.GetCurrentDirectory(),
            string.Empty),
        "EmailReader.TenantId");

    options.EmailReader.TenantId = "11111111-1111-4111-8111-111111111111";
    options.EmailReader.ClientId = "22222222-2222-4222-8222-222222222222";
    options.EmailReader.ClientSecret = "segredo-somente-de-teste";
    _ = RpaWorkerOptionsValidator.Validate(options, Directory.GetCurrentDirectory(), string.Empty);

    provider.SenderAddress = "remetente inválido";
    AssertInvalid(
        () => RpaWorkerOptionsValidator.Validate(
            options,
            Directory.GetCurrentDirectory(),
            string.Empty),
        "SenderAddress");
    provider.SenderAddress = "nao-responda@sistema.com.br";

    provider.CodePattern = "(";
    AssertInvalid(
        () => RpaWorkerOptionsValidator.Validate(
            options,
            Directory.GetCurrentDirectory(),
            string.Empty),
        "expressão regular válida");
    Pass("configuração Graph, e-mail e expressão regular são validadas");
}

static void CheckParsingAndNewestMessage()
{
    var expression = new Regex(
        @"(?:código|token)\s*[-:]\s*(\d{6})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    var extracted = MicrosoftGraphEmailOneTimeCodeProvider.ExtractCode(
        expression,
        "<p>Código: 987654</p>");
    Check(extracted == "987654", "o código é extraído do HTML");

    var start = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
    var options = CreateEnabledProvider();
    var messages = new[]
    {
        Message("Código de autenticação", "111111", start.AddSeconds(10)),
        Message("Código de autenticação", "222222", start.AddSeconds(20)),
        Message(
            "Código de autenticação",
            "333333",
            start.AddSeconds(30),
            "outro@sistema.com.br")
    };
    var newest = MicrosoftGraphEmailOneTimeCodeProvider.FindNewestMatchingCode(
        messages,
        expression,
        options,
        start,
        start.AddMinutes(1),
        CancellationToken.None);
    Check(
        newest?.Code == "222222" && newest.ReceivedAt == start.AddSeconds(20),
        "a mensagem válida mais recente é selecionada");

    var filter = MicrosoftGraphEmailOneTimeCodeProvider.BuildFilter(
        new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.FromHours(-3)),
        new DateTimeOffset(2026, 7, 31, 10, 5, 0, TimeSpan.FromHours(-3)),
        "Código d' acesso");
    Check(
        filter.Contains("receivedDateTime ge 2026-07-31T13:00:00Z", StringComparison.Ordinal) &&
        filter.Contains("receivedDateTime le 2026-07-31T13:05:00Z", StringComparison.Ordinal) &&
        filter.Contains("contains(subject, 'Código d'' acesso')", StringComparison.Ordinal),
        "o filtro do Graph preserva janela UTC e escapa apóstrofo");
}

static async Task CheckPollingTimeoutAndAliasLockAsync()
{
    var options = CreateWorkerOptions();
    options.EmailReader.Providers["email-otp"] = CreateEnabledProvider();
    var requestedAt = new DateTimeOffset(2026, 7, 31, 15, 0, 0, TimeSpan.Zero);
    var captures = 0;
    var pollingProvider = new MicrosoftGraphEmailOneTimeCodeProvider(
        options,
        (_, notBefore, _) =>
        {
            var attempt = Interlocked.Increment(ref captures);
            return Task.FromResult<OneTimeCodeResult?>(
                attempt < 3
                    ? null
                    : new OneTimeCodeResult("654321", notBefore.AddSeconds(1)));
        });
    var result = await pollingProvider.WaitForCodeAsync(
        Request(requestedAt, TimeSpan.FromSeconds(1)),
        CancellationToken.None);
    Check(result.Code == "654321" && captures == 3, "o polling continua até encontrar o código");

    var timeoutProvider = new MicrosoftGraphEmailOneTimeCodeProvider(
        options,
        (_, _, _) => Task.FromResult<OneTimeCodeResult?>(null));
    try
    {
        await timeoutProvider.WaitForCodeAsync(
            Request(requestedAt, TimeSpan.FromMilliseconds(40)),
            CancellationToken.None);
        throw new InvalidOperationException("O provider não respeitou o timeout total.");
    }
    catch (TimeoutException)
    {
        Pass("o timeout total interrompe o polling");
    }

    var firstCaptureEntered = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirstCapture = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var activeCaptures = 0;
    var maximumConcurrentCaptures = 0;
    var captureCalls = 0;
    var lockedProvider = new MicrosoftGraphEmailOneTimeCodeProvider(
        options,
        async (_, notBefore, cancellationToken) =>
        {
            var active = Interlocked.Increment(ref activeCaptures);
            UpdateMaximum(ref maximumConcurrentCaptures, active);
            var call = Interlocked.Increment(ref captureCalls);
            try
            {
                if (call == 1)
                {
                    firstCaptureEntered.TrySetResult(true);
                    await releaseFirstCapture.Task.WaitAsync(cancellationToken);
                }

                return new OneTimeCodeResult(
                    call == 1 ? "111111" : "222222",
                    notBefore.AddSeconds(call));
            }
            finally
            {
                Interlocked.Decrement(ref activeCaptures);
            }
        });
    var lockedRequest = Request(requestedAt, TimeSpan.FromSeconds(2));
    var firstWait = lockedProvider.WaitForCodeAsync(lockedRequest, CancellationToken.None);
    await firstCaptureEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    var secondWait = lockedProvider.WaitForCodeAsync(lockedRequest, CancellationToken.None);
    await Task.Delay(30);
    Check(
        captureCalls == 1 && maximumConcurrentCaptures == 1,
        "duas esperas do mesmo alias não consultam a caixa simultaneamente");
    releaseFirstCapture.TrySetResult(true);
    await Task.WhenAll(firstWait, secondWait);
    Check(
        captureCalls == 2 && maximumConcurrentCaptures == 1,
        "o lock cobre toda a janela de polling do alias");
}

static void CheckFlowProviderFences()
{
    var options = CreateWorkerOptions();
    var definition = options.Definitions["exemplo"];
    var flow = new FlowDefinition
    {
        SchemaVersion = 1,
        Name = "Fluxo com OTP",
        Actions =
        [
            new FlowActionDefinition
            {
                Id = "aguardar-otp",
                Type = "waitForOneTimeCode",
                Name = "Aguardar OTP",
                ProviderAlias = "email-otp",
                Target = "runtime.authentication.otp"
            }
        ]
    };

    AssertInvalid(
        () => RpaWorkerOptionsValidator.ValidateOneTimeCodeProviders(
            options,
            "exemplo",
            definition,
            flow),
        "não existe");

    options.EmailReader.Providers["email-otp"] = new EmailOneTimeCodeProviderOptions();
    RpaWorkerOptionsValidator.ValidateOneTimeCodeProviders(
        options,
        "exemplo",
        definition,
        flow);

    definition.ClaimEnabled = true;
    AssertInvalid(
        () => RpaWorkerOptionsValidator.ValidateOneTimeCodeProviders(
            options,
            "exemplo",
            definition,
            flow),
        "desabilitado");

    options.EmailReader.Providers["email-otp"].Enabled = true;
    options.MaxParallelism = 2;
    AssertInvalid(
        () => RpaWorkerOptionsValidator.ValidateOneTimeCodeProviders(
            options,
            "exemplo",
            definition,
            flow),
        "MaxParallelism igual a 1");

    options.MaxParallelism = 1;
    definition.MfaAttemptActionIds = ["aguardar-otp"];
    RpaWorkerOptionsValidator.ValidateOneTimeCodeProviders(
        options,
        "exemplo",
        definition,
        flow);
    Pass("alias, habilitação e paralelismo são cercados antes do claim");
}

static void CheckOneTimeCodeOutputSanitization()
{
    var options = CreateWorkerOptions();
    options.MaxParallelism = 1;
    options.EmailReader.Providers["email-otp"] = CreateEnabledProvider();
    var definition = options.Definitions["exemplo"];
    var flow = new FlowDefinition
    {
        SchemaVersion = 1,
        Name = "Fluxo com OTP",
        Actions =
        [
            new FlowActionDefinition
            {
                Id = "aguardar-otp",
                Type = "waitForOneTimeCode",
                Name = "Aguardar OTP",
                ProviderAlias = "email-otp",
                Target = "runtime.authentication.otp"
            }
        ]
    };
    definition.Outputs.Add(new OutputMappingOptions
    {
        Name = "autenticacao",
        Source = "runtime.authentication"
    });
    AssertInvalid(
        () => RpaWorkerOptionsValidator.ValidateOneTimeCodeProviders(
            options,
            "exemplo",
            definition,
            flow),
        "código temporário");
    definition.Outputs.Clear();

    var output = new System.Text.Json.Nodes.JsonObject
    {
        ["authentication"] = new System.Text.Json.Nodes.JsonObject
        {
            ["otp"] = "654321",
            ["trustedDevice"] = true
        },
        ["protocol"] = "ABC-123"
    };
    var sanitized = SensitiveRuntimeOutputSanitizer.RedactOneTimeCodes(output, flow);
    Check(
        sanitized["authentication"]?["otp"] is null &&
        sanitized["authentication"]?["trustedDevice"]?.GetValue<bool>() == true &&
        sanitized["protocol"]?.GetValue<string>() == "ABC-123" &&
        output["authentication"]?["otp"]?.GetValue<string>() == "654321",
        "o OTP é removido da cópia persistida sem alterar o runtime original");
}

static RpaWorkerOptions CreateWorkerOptions()
{
    var options = new RpaWorkerOptions
    {
        WorkspaceRoot = "."
    };
    options.Definitions["exemplo"] = new RpaDefinitionOptions
    {
        FlowFile = "flow.production.json"
    };
    return options;
}

static EmailOneTimeCodeProviderOptions CreateEnabledProvider() => new()
{
    Enabled = true,
    Mailbox = "rpa@empresa.com.br",
    SenderAddress = "nao-responda@sistema.com.br",
    SubjectContains = "Código de autenticação",
    CodePattern = @"(?:código|token)\s*[-:]\s*(\d{6})",
    MaximumEmailAgeMinutes = 5,
    RequestedEmailCount = 10
};

static Message Message(
    string subject,
    string code,
    DateTimeOffset receivedAt,
    string sender = "nao-responda@sistema.com.br") => new()
{
    Subject = subject,
    ReceivedDateTime = receivedAt,
    From = new Recipient
    {
        EmailAddress = new EmailAddress { Address = sender }
    },
    Body = new ItemBody { Content = $"<p>Código: {code}</p>" },
    BodyPreview = $"Código: {code}"
};

static OneTimeCodeRequest Request(DateTimeOffset requestedAt, TimeSpan timeout) => new(
    "email-otp",
    requestedAt,
    timeout,
    TimeSpan.FromMilliseconds(5));

static void UpdateMaximum(ref int maximum, int candidate)
{
    while (true)
    {
        var current = Volatile.Read(ref maximum);
        if (candidate <= current ||
            Interlocked.CompareExchange(ref maximum, candidate, current) == current)
        {
            return;
        }
    }
}

static void AssertInvalid(Action action, string expectedMessage)
{
    try
    {
        action();
    }
    catch (InvalidOperationException exception)
        when (exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
    {
        Pass($"falha esperada contém '{expectedMessage}'");
        return;
    }

    throw new InvalidOperationException(
        $"Era esperada uma falha contendo '{expectedMessage}'.");
}

static async Task AssertInvalidAsync(
    Func<Task> action,
    string expectedMessage)
{
    try
    {
        await action();
    }
    catch (InvalidOperationException exception)
        when (exception.Message.Contains(
            expectedMessage,
            StringComparison.OrdinalIgnoreCase))
    {
        Pass($"falha esperada contém '{expectedMessage}'");
        return;
    }

    throw new InvalidOperationException(
        $"Era esperada uma falha contendo '{expectedMessage}'.");
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

    throw new DirectoryNotFoundException(
        "Não foi possível localizar a raiz do repositório.");
}

static void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Falha: {description}.");
    }

    Pass(description);
}

static void Pass(string description) => Console.WriteLine($"OK: {description}.");
