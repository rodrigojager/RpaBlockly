using System.Text.Json.Nodes;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Graph.Models;
using Rpa.Worker.Authentication;
using Rpa.Worker.Configuration;
using Rpa.Worker.Execution;
using Rpa.Worker.Domain;
using Rpa.Worker.Hosting;
using Rpa.Worker.Data;
using RpaFlow.Contracts;
using RpaFlow.Playwright;
using RpaFlow.Playwright.V2;
using RpaFlow.Packages;
using RpaFlow.Runtime;
using V2 = RpaFlow.Contracts.V2;

Console.OutputEncoding = Encoding.UTF8;
CheckBrowserDefaults();
CheckDisabledProviderDoesNotRequireCredentials();
CheckEnabledProviderConfiguration();
CheckCaptchaConfiguration();
CheckParsingAndNewestMessage();
await CheckPollingTimeoutAndAliasLockAsync();
CheckFlowProviderFences();
CheckOneTimeCodeOutputSanitization();
CheckConfiguredExecutionGuard();
await CheckWorkerFailurePolicyAsync();
CheckWorkerReadiness();
await CheckSafeValidationBoundaryConfigurationAsync();
var sqlConnection = ArgumentValue(args, "--sql-connection");
var sqlSchema = ArgumentValue(args, "--sql-schema");
if (sqlConnection is not null && sqlSchema is not null)
{
    await CheckSqlLeaseFencingAsync(sqlConnection, sqlSchema);
}
Console.WriteLine("Worker e provider de OTP por e-mail validados com sucesso.");

static void CheckBrowserDefaults()
{
    var runtime = new RpaRuntimeOptions();
    Check(
        runtime.Browser == PlaywrightBrowserSelection.DefaultValue &&
        runtime.Browser == "spybrowser" &&
        runtime.SpyBrowserHumanize,
        "worker usa SpyBrowser humanizado por padrão");
    Check(
        new[] { "chromium", "firefox", "webkit", "chrome", "msedge", "cloakbrowser" }
            .All(PlaywrightBrowserSelection.IsSupported),
        "seleções de navegador anteriores continuam disponíveis");
}

static async Task CheckSqlLeaseFencingAsync(string connectionString, string schema)
{
    var options = CreateWorkerOptions();
    options.WorkerId = "shared-worker";
    options.LeaseSeconds = 30;
    options.RetryDelaySeconds = 0;
    options.Tables.Schema = schema;
    options.Definitions.Clear();
    options.Definitions["lease-test"] = new RpaDefinitionOptions
    {
        Enabled = true,
        ClaimEnabled = true,
        AuthenticationAttemptActionIds = ["auth-action"],
        MfaAttemptActionIds = ["mfa-action"],
        IrreversibleActionIds = ["irreversible-action"]
    };
    var root = Directory.GetCurrentDirectory();
    var paths = new WorkerPaths(root, root, root, root);
    var repository = new SqlWorkItemRepository(
        options,
        new WorkerEnvironment(connectionString, paths));
    await repository.ValidateSchemaAsync(CancellationToken.None);

    var packageOptions = CreateWorkerOptions();
    packageOptions.Tables.Schema = schema;
    packageOptions.Definitions["exemplo"].Package = new RpaPackageReferenceOptions
    {
        RpaId = "same-rpa",
        Provider = "SqlServer",
        OriginName = "source",
        Location = "integration-test"
    };
    var packageRegistry = RpaPackageRegistryFactory.Create(
        packageOptions,
        new WorkerEnvironment(connectionString, paths));
    var currentPackage = await packageRegistry.ResolveAsync(
        "same-rpa", "source", null, CancellationToken.None);
    packageOptions.Definitions["exemplo"].Package!.Revision =
        currentPackage.Revision.Value.ToLowerInvariant();
    await RpaWorkerOptionsValidator.ValidateFlowsAsync(
        packageOptions,
        paths,
        CancellationToken.None,
        packageRegistry);
    Pass("provider SQL usa o schema do worker e normaliza revisão SHA-256 fixada");

    await using (var connection = new SqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO [{schema}].[WorkItem] (RpaCode) VALUES (N'lease-test');";
        await insert.ExecuteNonQueryAsync();
    }

    var first = await repository.ClaimNextAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("O primeiro claim SQL não retornou o item.");
    await repository.StartExecutionAsync("lease-first", first, CancellationToken.None);
    await ExpireLeaseAsync(connectionString, schema, first.WorkItemId);
    await ExpectFailureAsync(
        () => repository.RenewLeaseAsync(first, CancellationToken.None),
        "lease expirada não pode ser ressuscitada pelo heartbeat atrasado");

    var second = await repository.ClaimNextAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("O reclaim SQL não retornou o item.");
    await repository.StartExecutionAsync("lease-second", second, CancellationToken.None);
    Check(first.LeaseToken != Guid.Empty && second.LeaseToken != first.LeaseToken,
        "cada claim SQL recebe LeaseToken exclusivo");

    await ExpectFailureAsync(
        () => repository.CompleteAsync(
            "lease-first", first, "Succeeded", "{}", 0, CancellationToken.None),
        "claim antigo não conclui item após reclaim com o mesmo WorkerId");
    await ExpectFailureAsync(
        () => repository.FailAsync(
            "lease-first",
            first,
            new WorkerFailureDecision("Failed", "TESTE", "stale", false),
            CancellationToken.None),
        "claim antigo não marca falha após reclaim");
    await ExpectFailureAsync(
        () => repository.SaveOutputsAsync(
            "lease-first",
            first,
            [new MaterializedOutput("stale", JsonValue.Create(true), false)],
            CancellationToken.None),
        "claim antigo não salva output após reclaim");
    await ExpectFailureAsync(
        () => repository.AppendEventAsync(
            LeaseEvent("actionCompleted", "lease-first", first),
            first,
            CancellationToken.None),
        "claim antigo não acrescenta evento após reclaim");

    var documents = new RpaPackageDocuments(
        new V2.FlowDefinition
        {
            Name = "Lease fencing",
            Actions =
            [
                new V2.FlowActionDefinition
                {
                    Id = "lease-action",
                    Type = "setVariable",
                    Name = "Lease action",
                    Value = System.Text.Json.JsonSerializer.SerializeToElement(true),
                    Output = "runtime.lease"
                }
            ]
        },
        new V2.LocatorCatalog(),
        new V2.RpaPolicyDefinition());
    var snapshot = new RpaPackageSnapshot(
        "lease-test",
        new PackageRevision(new string('A', 64)),
        documents,
        new RpaPackageOrigin("sqlserver", "integration-test"));
    await repository.SetExecutionPackageAsync(
        "lease-second", second, snapshot.Origin.Kind, snapshot, CancellationToken.None);
    var persistedIdentity = await ReadExecutionIdentityAsync(
        connectionString, schema, "lease-second");
    Check(
        persistedIdentity.Origin == snapshot.Origin.Kind &&
        persistedIdentity.Revision == snapshot.Revision.Value &&
        persistedIdentity.Hash == snapshot.ContentHash,
        "execução persiste exatamente a origem, revisão e hash do snapshot fixado");
    await repository.AppendEventAsync(
        LeaseEvent("actionStarted", "lease-second", second),
        second,
        CancellationToken.None);
    await repository.SaveOutputsAsync(
        "lease-second",
        second,
        [new MaterializedOutput("current", JsonValue.Create(true), false)],
        CancellationToken.None);
    await repository.SaveArtifactsAsync(
        "lease-second",
        second,
        [new MaterializedArtifact("proof", "text", "proof.txt", 1, new string('B', 64))],
        CancellationToken.None);
    await repository.CompleteAsync(
        "lease-second", second, "Succeeded", "{}", 1, CancellationToken.None);
    await ExpectFailureAsync(
        () => repository.AppendEventAsync(
            LeaseEvent("actionCompleted", "lease-second", second),
            second,
            CancellationToken.None),
        "evento operacional tardio é rejeitado depois da conclusão");
    Pass("repositório SQL aplica fencing em heartbeat, payloads, eventos e terminalização");

    await CheckForeignRecoveryIsIgnoredAsync(repository, connectionString, schema);
    await CheckRecoveryUsesPinnedPolicyAsync(
        repository, options, connectionString, schema);
    await CheckRecoveryBlockedAsync(
        repository,
        connectionString,
        schema,
        "recovery-auth",
        [RecoveryEventSpec("actionStarted", "auth-action", "click")],
        "REPETICAO_DE_LOGIN_BLOQUEADA");
    await CheckRecoveryBlockedAsync(
        repository,
        connectionString,
        schema,
        "recovery-mfa",
        [RecoveryEventSpec("actionStarted", "mfa-action", "fill")],
        "REPETICAO_DE_MFA_BLOQUEADA");
    await CheckRecoveryBlockedAsync(
        repository,
        connectionString,
        schema,
        "recovery-handoff",
        [RecoveryEventSpec("captchaHumanHandoffRequested", "captcha", "solveCaptcha")],
        "INTERVENCAO_HUMANA_PENDENTE");
    await CheckRecoveryBlockedAsync(
        repository,
        connectionString,
        schema,
        "recovery-irreversible",
        [RecoveryEventSpec("actionCompleted", "irreversible-action", "click")],
        "EFEITO_IRREVERSIVEL_CONCLUIDO");
    await CheckRecoveryAllowedAsync(repository, connectionString, schema);
    await CheckConcurrentRecoveryAsync(repository, connectionString, schema);
    Pass("recuperação de lease consulta o histórico antes de autorizar repetição");
}

static async Task CheckConcurrentRecoveryAsync(
    SqlWorkItemRepository repository,
    string connectionString,
    string schema)
{
    await InsertWorkItemAsync(connectionString, schema);
    var expired = await repository.ClaimNextAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("O claim concorrente inicial não retornou item.");
    await repository.StartExecutionAsync(
        "recovery-concurrent-expired", expired, CancellationToken.None);
    await ExpireLeaseAsync(connectionString, schema, expired.WorkItemId);

    var gateResource = $"rpablockly-recovery-{schema}";
    await CreateRecoveryGateTriggerAsync(connectionString, schema, gateResource);
    RpaWorkItem? reclaimed = null;
    try
    {
        await using var blocker = new SqlConnection(connectionString);
        await blocker.OpenAsync();
        await using var blockerTransaction = (SqlTransaction)await blocker.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted);
        var blockerSessionId = 0;
        await using (var command = blocker.CreateCommand())
        {
            command.Transaction = blockerTransaction;
            command.CommandText = "SELECT @@SPID; DECLARE @result int; " +
                "EXEC @result=sys.sp_getapplock @Resource=@resource, " +
                "@LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=5000; " +
                "SELECT @result;";
            command.Parameters.AddWithValue("@resource", gateResource);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            blockerSessionId = Convert.ToInt32(reader.GetValue(0));
            await reader.NextResultAsync();
            await reader.ReadAsync();
            Check(reader.GetInt32(0) >= 0,
                "teste concorrente arma o gate interno do recovery");
        }

        var firstClaim = repository.ClaimNextAsync(CancellationToken.None);
        try
        {
            await WaitForBlockedSessionAsync(connectionString, blockerSessionId);
            var second = await repository.ClaimNextAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Check(second is null,
                "segundo recovery pula candidato já travado antes da atualização");
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }

        reclaimed = await firstClaim ??
            await repository.ClaimNextAsync(CancellationToken.None);
    }
    finally
    {
        await DropRecoveryGateTriggerAsync(connectionString, schema);
    }
    if (reclaimed is null)
    {
        throw new InvalidOperationException("Nenhum polling reservou o item recuperado.");
    }
    Check(reclaimed.LeaseToken != expired.LeaseToken,
        "recovery concorrente produz um único claim com token novo");
    await repository.StartExecutionAsync(
        "recovery-concurrent-current", reclaimed, CancellationToken.None);
    await repository.RenewLeaseAsync(reclaimed, CancellationToken.None);
    await repository.CompleteAsync(
        "recovery-concurrent-current",
        reclaimed,
        "Succeeded",
        "{}",
        0,
        CancellationToken.None);
    Pass("recovery concorrente não revoga o lease recém-atribuído");
}

static async Task CreateRecoveryGateTriggerAsync(
    string connectionString,
    string schema,
    string gateResource)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = $"""
        CREATE OR ALTER TRIGGER [{schema}].[RecoveryConcurrencyGate]
        ON [{schema}].[WorkItem]
        AFTER UPDATE
        AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                  FROM inserted AS currentItem
                  INNER JOIN deleted AS previousItem
                          ON previousItem.WorkItemId = currentItem.WorkItemId
                 WHERE previousItem.Status = N'Running'
                   AND currentItem.Status IN (N'Retry', N'Failed')
                   AND previousItem.LeaseExpiresAtUtc < SYSUTCDATETIME()
            )
            BEGIN
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock
                    @Resource=N'{gateResource}',
                    @LockMode=N'Shared',
                    @LockOwner=N'Transaction',
                    @LockTimeout=10000;
                IF @result < 0
                    THROW 51000, N'O gate concorrente de teste expirou.', 1;
            END
        END;
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task DropRecoveryGateTriggerAsync(string connectionString, string schema)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = $"DROP TRIGGER IF EXISTS [{schema}].[RecoveryConcurrencyGate];";
    await command.ExecuteNonQueryAsync();
}

static async Task WaitForBlockedSessionAsync(
    string connectionString,
    int blockerSessionId)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
    while (DateTimeOffset.UtcNow < deadline)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests " +
            "WHERE blocking_session_id=@blockerSessionId;";
        command.Parameters.AddWithValue("@blockerSessionId", blockerSessionId);
        if ((int)(await command.ExecuteScalarAsync() ?? 0) > 0)
        {
            return;
        }
        await Task.Delay(20);
    }

    throw new InvalidOperationException(
        "O primeiro recovery não entrou na seção crítica bloqueada pelo teste.");
}

static async Task CheckRecoveryBlockedAsync(
    SqlWorkItemRepository repository,
    string connectionString,
    string schema,
    string executionId,
    IReadOnlyList<(string Kind, string ActionId, string ActionType)> events,
    string expectedReason)
{
    await InsertWorkItemAsync(connectionString, schema);
    var item = await repository.ClaimNextAsync(CancellationToken.None)
        ?? throw new InvalidOperationException($"O claim {executionId} não retornou item.");
    await repository.StartExecutionAsync(executionId, item, CancellationToken.None);
    foreach (var executionEvent in events)
    {
        await repository.AppendEventAsync(
            BuildRecoveryEvent(executionId, item, executionEvent),
            item,
            CancellationToken.None);
    }
    await ExpireLeaseAsync(connectionString, schema, item.WorkItemId);

    var reclaimed = await repository.ClaimNextAsync(CancellationToken.None);
    Check(reclaimed is null,
        $"lease {executionId} com cerca histórica não volta à fila");
    var state = await ReadWorkItemStateAsync(connectionString, schema, item.WorkItemId);
    Check(state.Status == "Failed" && state.ErrorType == expectedReason && state.Completed,
        $"lease {executionId} termina com a razão histórica correta");
}

static async Task CheckRecoveryAllowedAsync(
    SqlWorkItemRepository repository,
    string connectionString,
    string schema)
{
    await InsertWorkItemAsync(connectionString, schema);
    var item = await repository.ClaimNextAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("O claim com cercas concluídas não retornou item.");
    const string executionId = "recovery-cleared";
    await repository.StartExecutionAsync(executionId, item, CancellationToken.None);
    var events = new[]
    {
        RecoveryEventSpec("actionStarted", "auth-action", "click"),
        RecoveryEventSpec("actionCompleted", "auth-completed", "completeAuthenticationAttempt"),
        RecoveryEventSpec("captchaHumanHandoffRequested", "captcha", "solveCaptcha"),
        RecoveryEventSpec("captchaHumanHandoffCompleted", "captcha", "solveCaptcha")
    };
    foreach (var executionEvent in events)
    {
        await repository.AppendEventAsync(
            BuildRecoveryEvent(executionId, item, executionEvent),
            item,
            CancellationToken.None);
    }
    await ExpireLeaseAsync(connectionString, schema, item.WorkItemId);

    var reclaimed = await repository.ClaimNextAsync(CancellationToken.None);
    if (reclaimed is null)
    {
        var state = await ReadWorkItemStateAsync(
            connectionString, schema, item.WorkItemId);
        Check(state.Status == "Retry" && state.ErrorType == "LEASE_EXPIRADO",
            "cercas concluídas liberam o item para o polling seguinte");
        reclaimed = await repository.ClaimNextAsync(CancellationToken.None);
    }
    if (reclaimed is null)
    {
        throw new InvalidOperationException(
            "Cercas concluídas impediram indevidamente a recuperação do lease.");
    }
    Check(reclaimed.WorkItemId == item.WorkItemId && reclaimed.LeaseToken != item.LeaseToken,
        "marcadores concluídos permitem reclaim com novo token");
    await repository.StartExecutionAsync(
        "recovery-cleared-reclaimed", reclaimed, CancellationToken.None);
    await repository.CompleteAsync(
        "recovery-cleared-reclaimed",
        reclaimed,
        "Succeeded",
        "{}",
        0,
        CancellationToken.None);
}

static async Task CheckForeignRecoveryIsIgnoredAsync(
    SqlWorkItemRepository repository,
    string connectionString,
    string schema)
{
    var workItemId = Guid.NewGuid();
    var leaseToken = Guid.NewGuid();
    await using (var connection = new SqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO [{schema}].[WorkItem]
                (WorkItemId, RpaCode, Status, AttemptCount, LeaseOwner, LeaseToken,
                 LeaseExpiresAtUtc)
            VALUES
                (@workItemId, N'foreign-rpa', N'Running', 1, N'foreign-worker',
                 @leaseToken, DATEADD(SECOND, -1, SYSUTCDATETIME()));
            INSERT INTO [{schema}].[Execution]
                (ExecutionId, WorkItemId, WorkerId, LeaseToken, Status, StartedAtUtc)
            VALUES
                (N'foreign-execution', @workItemId, N'foreign-worker', @leaseToken,
                 N'Running', DATEADD(MINUTE, -1, SYSUTCDATETIME()));
            """;
        command.Parameters.AddWithValue("@workItemId", workItemId);
        command.Parameters.AddWithValue("@leaseToken", leaseToken);
        await command.ExecuteNonQueryAsync();
    }

    var claim = await repository.ClaimNextAsync(CancellationToken.None);
    Check(claim is null,
        "worker não reserva RPA fora da própria configuração");
    var state = await ReadWorkItemStateAsync(connectionString, schema, workItemId);
    Check(state.Status == "Running" && state.ErrorType is null && !state.Completed,
        "worker não recupera lease de RPA cujas cercas desconhece");
}

static async Task CheckRecoveryUsesPinnedPolicyAsync(
    SqlWorkItemRepository repository,
    RpaWorkerOptions options,
    string connectionString,
    string schema)
{
    await InsertWorkItemAsync(connectionString, schema);
    var item = await repository.ClaimNextAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("O claim da política fixada não retornou item.");
    const string executionId = "recovery-pinned-policy";
    await repository.StartExecutionAsync(executionId, item, CancellationToken.None);
    await repository.AppendEventAsync(
        BuildRecoveryEvent(
            executionId,
            item,
            RecoveryEventSpec("actionStarted", "auth-action", "click")),
        item,
        CancellationToken.None);
    await ExpireLeaseAsync(connectionString, schema, item.WorkItemId);

    var definition = options.Definitions["lease-test"];
    var original = definition.AuthenticationAttemptActionIds;
    try
    {
        definition.AuthenticationAttemptActionIds = ["auth-action-v2"];
        var reclaimed = await repository.ClaimNextAsync(CancellationToken.None);
        Check(reclaimed is null,
            "alteração posterior da definição não reinterpreta a execução expirada");
        var state = await ReadWorkItemStateAsync(
            connectionString, schema, item.WorkItemId);
        Check(state.Status == "Failed" &&
              state.ErrorType == "REPETICAO_DE_LOGIN_BLOQUEADA" &&
              state.Completed,
            "recovery usa a política persistida no início da execução");
    }
    finally
    {
        definition.AuthenticationAttemptActionIds = original;
    }
}

static (string Kind, string ActionId, string ActionType) RecoveryEventSpec(
    string kind,
    string actionId,
    string actionType) =>
    (kind, actionId, actionType);

static FlowExecutionEvent BuildRecoveryEvent(
    string executionId,
    RpaWorkItem item,
    (string Kind, string ActionId, string ActionType) value) =>
    new(
        value.Kind,
        executionId,
        item.WorkItemId.ToString(),
        null,
        DateTimeOffset.UtcNow,
        value.ActionId,
        value.ActionId,
        value.ActionType);

static FlowExecutionEvent LeaseEvent(string kind, string executionId, RpaWorkItem item) =>
    new(
        kind,
        executionId,
        item.WorkItemId.ToString(),
        null,
        DateTimeOffset.UtcNow,
        "lease-action",
        "Lease action",
        "setVariable");

static async Task InsertWorkItemAsync(string connectionString, string schema)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText =
        $"INSERT INTO [{schema}].[WorkItem] (RpaCode) VALUES (N'lease-test');";
    await command.ExecuteNonQueryAsync();
}

static async Task ExpireLeaseAsync(string connectionString, string schema, Guid workItemId)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = $"UPDATE [{schema}].[WorkItem] " +
        "SET LeaseExpiresAtUtc=DATEADD(SECOND,-1,SYSUTCDATETIME()) " +
        "WHERE WorkItemId=@workItemId;";
    command.Parameters.AddWithValue("@workItemId", workItemId);
    await command.ExecuteNonQueryAsync();
}

static async Task<(string Origin, string Revision, string Hash)> ReadExecutionIdentityAsync(
    string connectionString,
    string schema,
    string executionId)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = $"SELECT RpaPackageOrigin,RpaPackageRevision,RpaPackageHash " +
        $"FROM [{schema}].[Execution] WHERE ExecutionId=@executionId;";
    command.Parameters.AddWithValue("@executionId", executionId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException("A execução SQL fixada não foi encontrada.");
    }
    return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
}

static async Task<(string Status, string? ErrorType, bool Completed)> ReadWorkItemStateAsync(
    string connectionString,
    string schema,
    Guid workItemId)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = $"SELECT Status,ErrorType,CompletedAtUtc " +
        $"FROM [{schema}].[WorkItem] WHERE WorkItemId=@workItemId;";
    command.Parameters.AddWithValue("@workItemId", workItemId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException("O item recuperado não foi encontrado.");
    }
    return (
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        !reader.IsDBNull(2));
}

static async Task ExpectFailureAsync(Func<Task> operation, string description)
{
    try
    {
        await operation();
    }
    catch (Exception exception) when (exception is InvalidOperationException or SqlException)
    {
        Pass(description);
        return;
    }

    throw new InvalidOperationException($"Falha: {description}.");
}

static string? ArgumentValue(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

static void CheckConfiguredExecutionGuard()
{
    var request = new FlowExecutionRequest("guard-worker", [], [], []);
    var ordinaryAction = new FlowActionIdentity(
        "preparar-evidencia",
        "setVariable",
        "Preparar evidência");
    var boundaryAction = new FlowActionIdentity(
        "registrar-limite-seguro",
        "screenshot",
        "Registrar limite seguro");
    var irreversibleAction = new FlowActionIdentity(
        "confirmar-operacao",
        "click",
        "Confirmar operação");
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

static async Task CheckWorkerFailurePolicyAsync()
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
    var failingObserver = new WorkerFlowExecutionObserver(
        repository, item, ["enviar-login"], []);
    await ExpectFailureAsync(
        () => failingObserver.ObserveAsync(new FlowExecutionEvent(
                "actionStarted", "execucao", item.WorkItemId.ToString(), null,
                DateTimeOffset.UtcNow, "enviar-login", "Enviar login", "click"),
                CancellationToken.None)
            .AsTask(),
        "falha de persistência do observer é reproduzida");
    Check(failingObserver.AuthenticationAttemptStarted,
        "falha de persistência não apaga proteção local contra repetição");
    await ExpectFailureAsync(
        () => failingObserver.ObserveAsync(new FlowExecutionEvent(
                "actionCompleted", "execucao", item.WorkItemId.ToString(), null,
                DateTimeOffset.UtcNow, "concluir-login", "Concluir login",
                "completeAuthenticationAttempt"),
                CancellationToken.None)
            .AsTask(),
        "falha de persistência do marcador de conclusão é reproduzida");
    Check(!failingObserver.AuthenticationAttemptCompleted,
        "marcador não persistido não libera retry local de autenticação");
    failingObserver.Track(new FlowExecutionEvent(
        "captchaHumanHandoffRequested", "execucao", item.WorkItemId.ToString(), null,
        DateTimeOffset.UtcNow, "captcha", "Captcha", "solveCaptcha"));
    await ExpectFailureAsync(
        () => failingObserver.ObserveAsync(new FlowExecutionEvent(
                "captchaHumanHandoffCompleted", "execucao", item.WorkItemId.ToString(), null,
                DateTimeOffset.UtcNow, "captcha", "Captcha", "solveCaptcha"),
                CancellationToken.None)
            .AsTask(),
        "falha de persistência da conclusão do handoff é reproduzida");
    Check(failingObserver.HumanHandoffPending,
        "handoff não persistido permanece cercado localmente");
    var observer = new WorkerFlowExecutionObserver(repository, item, ["enviar-login"], []);
    observer.Track(new FlowExecutionEvent(
        "captchaHumanHandoffRequested", "execucao", item.WorkItemId.ToString(), null,
        DateTimeOffset.UtcNow, "captcha", "Captcha", "solveCaptcha"));
    var pending = WorkerFailurePolicy.Decide(
        new OperationCanceledException("Prazo global encerrado durante o handoff."),
        item, definition, observer, false, false);
    Check(!pending.Retry && pending.ErrorCode == "INTERVENCAO_HUMANA_PENDENTE",
        "timeout humano não repete automaticamente o fluxo inteiro");
    var pendingLeadership = WorkerFailurePolicy.Decide(
        new OperationCanceledException("Trava perdida durante o handoff."),
        item, definition, observer, false, true);
    Check(pendingLeadership.Retry && pendingLeadership.PreserveAttempt,
        "perda de liderança durante handoff preserva a tentativa");
    observer.Track(new FlowExecutionEvent(
        "captchaHumanHandoffCompleted", "execucao", item.WorkItemId.ToString(), null,
        DateTimeOffset.UtcNow, "captcha", "Captcha", "solveCaptcha"));
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
    var irreversible = WorkerFailurePolicy.Decide(
        transient,
        item,
        definition,
        observer,
        workerStopping: false,
        leadershipLost: false,
        irreversibleEffectCompleted: true);
    Check(!irreversible.Retry &&
          irreversible.ErrorCode == "EFEITO_IRREVERSIVEL_CONCLUIDO",
        "efeito irreversível concluído bloqueia qualquer repetição automática");
    Check(V2FlowActionHandlerRegistry.Default.SupportedTypes.Contains(
            "completeAuthenticationAttempt"),
        "runtime V2 possui handler para o marcador de autenticação");
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
    definition.Package = new RpaPackageReferenceOptions
    {
        RpaId = "rpa-exemplo",
        Provider = "File",
        OriginName = "source",
        Location = "examples/RpaExemplo/package-store"
    };
    definition.SafeValidationBoundaryActionId = "iniciar-fluxo";
    var paths = new WorkerPaths(
        repositoryRoot,
        repositoryRoot,
        Path.Combine(repositoryRoot, "artifacts"),
        Path.Combine(repositoryRoot, "storage", "sessions"));
    var registry = RpaPackageRegistryFactory.Create(
        options,
        new WorkerEnvironment(string.Empty, paths));
    await RpaWorkerOptionsValidator.ValidateFlowsAsync(
        options,
        paths,
        CancellationToken.None,
        registry);
    Pass("o worker aceita um limite seguro que referencia uma ação existente");

    definition.SafeValidationBoundaryActionId = "acao-inexistente";
    await AssertInvalidAsync(
        () => RpaWorkerOptionsValidator.ValidateFlowsAsync(
            options,
            paths,
            CancellationToken.None,
            registry),
        "SafeValidationBoundaryActionId referencia a ação inexistente");

    definition.SafeValidationBoundaryActionId = "iniciar-fluxo";
    definition.IrreversibleActionIds = ["iniciar-fluxo"];
    await AssertInvalidAsync(
        () => RpaWorkerOptionsValidator.ValidateFlowsAsync(
            options,
            paths,
            CancellationToken.None,
            registry),
        "não pode ser também uma ação irreversível");

    definition.IrreversibleActionIds = [];
    definition.SafeValidationBoundaryActionId = "executar-subfluxo";
    AssertInvalid(
        () => RpaWorkerOptionsValidator.ValidateSafeValidationBoundary(
            "exemplo",
            definition,
            [
                new FlowActionIdentity(
                    "executar-subfluxo",
                    "runSubflow",
                    "Executar subfluxo")
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

static void CheckCaptchaConfiguration()
{
    var options = CreateWorkerOptions();
    options.Definitions["exemplo"].Runtime.Captcha = new RpaCaptchaWorkerOptions
    {
        ServiceUrl = "url-inválida"
    };
    AssertInvalid(
        () => RpaWorkerOptionsValidator.Validate(
            options,
            Directory.GetCurrentDirectory(),
            string.Empty),
        "Captcha.ServiceUrl");

    var captcha = options.Definitions["exemplo"].Runtime.Captcha =
        new RpaCaptchaWorkerOptions
        {
            ServiceUrl = "http://127.0.0.1:8855",
            HCaptchaMaxAttempts = 4,
            DeadlineSeconds = 75,
            ServiceRetryAttempts = 3,
            MaximumImagePixels = 1_000_000,
            SliderMinimumScore = 0.7,
            LocalOnly = false,
            AutoSolveEnabled = true,
            AllowVlmFallback = true,
            SamePageWaitSeconds = 20,
            CloudflareSidecarEnabled = true,
            CloudflareSidecarProvider = "flaresolverr",
            CloudflareSidecarUrl = "http://127.0.0.1:8191",
            CloudflareSidecarTimeoutSeconds = 45,
            CloudflareSidecarMaximumResponseBytes = 2048,
            CloudflareSidecarAllowedHosts = ["portal.exemplo"]
        };
    var runtime = captcha.ToRuntimeOptions();
    Check(runtime.HCaptchaMaxAttempts == 4 &&
          runtime.DeadlineSeconds == 75 &&
          runtime.ServiceRetryAttempts == 3 &&
          runtime.MaximumImagePixels == 1_000_000 &&
          Math.Abs(runtime.SliderMinimumScore - 0.7) < 0.001 &&
          !runtime.LocalOnly && runtime.AutoSolveEnabled && runtime.AllowVlmFallback &&
          runtime.SamePageWaitSeconds == 20 && runtime.CloudflareSidecarEnabled &&
          runtime.CloudflareSidecarProvider == "flaresolverr" &&
          runtime.CloudflareSidecarTimeoutSeconds == 45 &&
          runtime.CloudflareSidecarMaximumResponseBytes == 2048 &&
          runtime.CloudflareSidecarAllowedHosts?.SequenceEqual(["portal.exemplo"]) == true,
        "worker propaga todas as opções de captcha ao runtime");

    captcha.SamePageWaitSeconds = 0;
    AssertInvalid(
        () => RpaWorkerOptionsValidator.Validate(
            options,
            Directory.GetCurrentDirectory(),
            string.Empty),
        "Captcha.SamePageWaitSeconds");
    captcha.SamePageWaitSeconds = 20;
    captcha.HCaptchaMaxAttempts = 0;
    AssertInvalid(
        () => RpaWorkerOptionsValidator.Validate(
            options,
            Directory.GetCurrentDirectory(),
            string.Empty),
        "Captcha.HCaptchaMaxAttempts");
    captcha.HCaptchaMaxAttempts = 4;
    captcha.CloudflareSidecarAllowedHosts = ["*"];
    AssertInvalid(
        () => RpaWorkerOptionsValidator.Validate(
            options,
            Directory.GetCurrentDirectory(),
            string.Empty),
        "Captcha.CloudflareSidecarAllowedHosts");
    Pass("configuração de captcha é validada e propagada antes do claim");
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
    var flow = new V2.FlowDefinition
    {
        SchemaVersion = 2,
        Name = "Fluxo com OTP",
        Actions =
        [
            new V2.FlowActionDefinition
            {
                Id = "aguardar-otp",
                Type = "waitForOneTimeCode",
                Name = "Aguardar OTP",
                ProviderAlias = "email-otp",
                Output = "runtime.authentication.otp"
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
    var flow = new V2.FlowDefinition
    {
        SchemaVersion = 2,
        Name = "Fluxo com OTP",
        Actions =
        [
            new V2.FlowActionDefinition
            {
                Id = "aguardar-otp",
                Type = "waitForOneTimeCode",
                Name = "Aguardar OTP",
                ProviderAlias = "email-otp",
                Output = "runtime.authentication.otp"
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
        Package = new RpaPackageReferenceOptions
        {
            RpaId = "exemplo",
            Provider = "File",
            OriginName = "source",
            Location = "package-store"
        }
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
