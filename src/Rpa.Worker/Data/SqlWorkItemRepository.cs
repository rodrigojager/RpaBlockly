using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Rpa.Worker.Configuration;
using Rpa.Worker.Domain;
using RpaFlow.Packages;
using RpaFlow.Runtime;

namespace Rpa.Worker.Data;

public sealed class SqlWorkItemRepository(
    RpaWorkerOptions options,
    WorkerEnvironment environment) : IWorkItemExecutionRepository
{
    private readonly string _connectionString = environment.ConnectionString;
    private readonly RpaWorkerOptions _options = options;
    private readonly string _workItems = Quote(options.Tables.Schema, options.Tables.WorkItems);
    private readonly string _executions = Quote(options.Tables.Schema, options.Tables.Executions);
    private readonly string _outputs = Quote(options.Tables.Schema, options.Tables.Outputs);
    private readonly string _artifacts = Quote(options.Tables.Schema, options.Tables.Artifacts);
    private readonly string _events = Quote(options.Tables.Schema, options.Tables.Events);
    private readonly string _workers = Quote(options.Tables.Schema, options.Tables.Workers);

    public async Task<RpaWorkItem?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var enabledCodes = _options.Definitions
            .Where(item => item.Value.Enabled && item.Value.ClaimEnabled)
            .Select(item => item.Key)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (enabledCodes.Length == 0)
        {
            return null;
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var codeParameters = string.Join(
            ", ",
            enabledCodes.Select((_, index) => $"@rpa{index}"));
        var sql = $"""
            DECLARE @now datetime2(3) = SYSUTCDATETIME();
            DECLARE @RecoveryCandidates TABLE
            (
                WorkItemId uniqueidentifier NOT NULL,
                LeaseToken uniqueidentifier NOT NULL,
                BlockReason nvarchar(200) NULL
            );
            DECLARE @Recovered TABLE
            (
                WorkItemId uniqueidentifier NOT NULL,
                LeaseToken uniqueidentifier NOT NULL,
                BlockReason nvarchar(200) NULL
            );
            DECLARE @Claimed TABLE
            (
                WorkItemId uniqueidentifier NOT NULL,
                RpaCode nvarchar(100) NOT NULL,
                BatchId nvarchar(100) NULL,
                SessionKey nvarchar(200) NULL,
                AttemptCount int NOT NULL,
                MaxAttempts int NOT NULL,
                InputJson nvarchar(max) NOT NULL,
                ConfigurationJson nvarchar(max) NOT NULL,
                AttachmentsJson nvarchar(max) NOT NULL,
                LeaseToken uniqueidentifier NOT NULL
            );

            INSERT INTO @RecoveryCandidates (WorkItemId, LeaseToken, BlockReason)
            SELECT workItem.WorkItemId,
                   workItem.LeaseToken,
                   CASE
                       WHEN ISNULL(history.RecoveryPolicyKnown, 0) = 0
                           THEN N'RECOVERY_POLICY_DESCONHECIDA'
                       WHEN history.LastIrreversibleCompleted IS NOT NULL
                           THEN N'EFEITO_IRREVERSIVEL_CONCLUIDO'
                       WHEN history.LastMfaStarted IS NOT NULL
                           THEN N'REPETICAO_DE_MFA_BLOQUEADA'
                       WHEN ISNULL(history.LastAuthenticationStarted, 0) >
                            ISNULL(history.LastAuthenticationCompleted, 0)
                           THEN N'REPETICAO_DE_LOGIN_BLOQUEADA'
                       WHEN ISNULL(history.LastHumanHandoffRequested, 0) >
                            ISNULL(history.LastHumanHandoffCompleted, 0)
                           THEN N'INTERVENCAO_HUMANA_PENDENTE'
                       ELSE NULL
                   END
              FROM {_workItems} AS workItem WITH (UPDLOCK, READPAST, ROWLOCK)
              CROSS APPLY
              (
                  SELECT
                      RecoveryPolicyKnown =
                      (
                          SELECT MIN(CASE
                              WHEN JSON_VALUE(execution.RecoveryPolicyJson, N'$.policyKnown') = N'true'
                                  THEN 1
                              ELSE 0
                          END)
                            FROM {_executions} AS execution
                           WHERE execution.WorkItemId = workItem.WorkItemId
                             AND execution.Status = N'Running'
                             AND execution.LeaseToken = workItem.LeaseToken
                      ),
                      LastAuthenticationStarted =
                      (
                          SELECT MAX(recordedEvent.ExecutionEventId)
                            FROM {_executions} AS execution
                            INNER JOIN {_events} AS recordedEvent
                                    ON recordedEvent.ExecutionId = execution.ExecutionId
                            CROSS APPLY OPENJSON(
                                execution.RecoveryPolicyJson,
                                N'$.authenticationAttemptActionIds') AS configured
                           WHERE execution.WorkItemId = workItem.WorkItemId
                              AND execution.Status = N'Running'
                              AND execution.LeaseToken = workItem.LeaseToken
                              AND configured.[value] = recordedEvent.ActionId
                              AND recordedEvent.Kind = N'actionStarted'
                      ),
                      LastAuthenticationCompleted =
                      (
                          SELECT MAX(recordedEvent.ExecutionEventId)
                            FROM {_executions} AS execution
                            INNER JOIN {_events} AS recordedEvent
                                    ON recordedEvent.ExecutionId = execution.ExecutionId
                            WHERE execution.WorkItemId = workItem.WorkItemId
                              AND execution.Status = N'Running'
                              AND execution.LeaseToken = workItem.LeaseToken
                              AND recordedEvent.Kind = N'actionCompleted'
                             AND recordedEvent.ActionType = N'completeAuthenticationAttempt'
                      ),
                      LastMfaStarted =
                      (
                          SELECT MAX(recordedEvent.ExecutionEventId)
                            FROM {_executions} AS execution
                            INNER JOIN {_events} AS recordedEvent
                                    ON recordedEvent.ExecutionId = execution.ExecutionId
                            CROSS APPLY OPENJSON(
                                execution.RecoveryPolicyJson,
                                N'$.mfaAttemptActionIds') AS configured
                           WHERE execution.WorkItemId = workItem.WorkItemId
                              AND execution.Status = N'Running'
                              AND execution.LeaseToken = workItem.LeaseToken
                              AND configured.[value] = recordedEvent.ActionId
                              AND recordedEvent.Kind = N'actionStarted'
                      ),
                      LastIrreversibleCompleted =
                      (
                          SELECT MAX(recordedEvent.ExecutionEventId)
                            FROM {_executions} AS execution
                            INNER JOIN {_events} AS recordedEvent
                                    ON recordedEvent.ExecutionId = execution.ExecutionId
                            CROSS APPLY OPENJSON(
                                execution.RecoveryPolicyJson,
                                N'$.irreversibleActionIds') AS configured
                            WHERE execution.WorkItemId = workItem.WorkItemId
                              AND execution.Status = N'Running'
                              AND execution.LeaseToken = workItem.LeaseToken
                              AND configured.[value] = recordedEvent.ActionId
                              AND recordedEvent.Kind = N'actionCompleted'
                      ),
                      LastHumanHandoffRequested =
                      (
                          SELECT MAX(recordedEvent.ExecutionEventId)
                            FROM {_executions} AS execution
                            INNER JOIN {_events} AS recordedEvent
                                    ON recordedEvent.ExecutionId = execution.ExecutionId
                            WHERE execution.WorkItemId = workItem.WorkItemId
                              AND execution.Status = N'Running'
                              AND execution.LeaseToken = workItem.LeaseToken
                              AND recordedEvent.Kind = N'captchaHumanHandoffRequested'
                      ),
                      LastHumanHandoffCompleted =
                      (
                          SELECT MAX(recordedEvent.ExecutionEventId)
                            FROM {_executions} AS execution
                            INNER JOIN {_events} AS recordedEvent
                                    ON recordedEvent.ExecutionId = execution.ExecutionId
                            WHERE execution.WorkItemId = workItem.WorkItemId
                              AND execution.Status = N'Running'
                              AND execution.LeaseToken = workItem.LeaseToken
                              AND recordedEvent.Kind = N'captchaHumanHandoffCompleted'
                      )
              ) AS history
             WHERE workItem.Status = N'Running'
               AND workItem.LeaseExpiresAtUtc < @now
               AND workItem.RpaCode IN ({codeParameters});

            UPDATE workItem
               SET Status = CASE
                       WHEN recovery.BlockReason IS NOT NULL OR
                            workItem.AttemptCount >= workItem.MaxAttempts
                           THEN N'Failed'
                       ELSE N'Retry'
                   END,
                   AvailableAtUtc = CASE
                       WHEN recovery.BlockReason IS NULL AND
                            workItem.AttemptCount < workItem.MaxAttempts
                           THEN DATEADD(SECOND, @retryDelaySeconds, @now)
                       ELSE workItem.AvailableAtUtc
                   END,
                   LeaseOwner = NULL,
                   LeaseToken = NULL,
                   LeaseExpiresAtUtc = NULL,
                   ErrorType = COALESCE(recovery.BlockReason, N'LEASE_EXPIRADO'),
                   ErrorMessage = CASE
                       WHEN recovery.BlockReason IS NOT NULL
                           THEN N'A execução anterior perdeu o heartbeat após uma etapa que bloqueia repetição automática.'
                       ELSE N'A execução anterior perdeu o heartbeat e foi recuperada automaticamente.'
                   END,
                   CompletedAtUtc = CASE
                       WHEN recovery.BlockReason IS NOT NULL OR
                            workItem.AttemptCount >= workItem.MaxAttempts
                           THEN @now
                       ELSE NULL
                   END,
                   UpdatedAtUtc = @now
            OUTPUT inserted.WorkItemId,
                   recovery.LeaseToken,
                   recovery.BlockReason
              INTO @Recovered (WorkItemId, LeaseToken, BlockReason)
              FROM {_workItems} AS workItem
              INNER JOIN @RecoveryCandidates AS recovery
                      ON recovery.WorkItemId = workItem.WorkItemId
             WHERE workItem.Status = N'Running'
               AND workItem.LeaseToken = recovery.LeaseToken
               AND workItem.LeaseExpiresAtUtc < @now;

            UPDATE execution
               SET Status = N'Failed',
                    ErrorType = COALESCE(recovered.BlockReason, N'LEASE_EXPIRADO'),
                    ErrorMessage = CASE
                        WHEN recovered.BlockReason IS NOT NULL
                            THEN N'A execução perdeu o heartbeat após uma etapa que bloqueia repetição automática.'
                        ELSE N'A execução perdeu o heartbeat.'
                    END,
                    CompletedAtUtc = @now
              FROM {_executions} AS execution
              INNER JOIN @Recovered AS recovered
                      ON recovered.WorkItemId = execution.WorkItemId
                     AND recovered.LeaseToken = execution.LeaseToken
             WHERE execution.Status = N'Running';

            ;WITH Candidate AS
            (
                SELECT TOP (1) *
                FROM {_workItems} WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status IN (N'Pending', N'Retry')
                  AND AvailableAtUtc <= SYSUTCDATETIME()
                  AND AttemptCount < MaxAttempts
                  AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc < SYSUTCDATETIME())
                  AND RpaCode IN ({codeParameters})
                ORDER BY Priority DESC, CreatedAtUtc, WorkItemId
            )
            UPDATE Candidate
               SET Status = N'Running',
                   LeaseOwner = @workerId,
                   LeaseToken = NEWID(),
                   LeaseExpiresAtUtc = DATEADD(SECOND, @leaseSeconds, SYSUTCDATETIME()),
                   AttemptCount = AttemptCount + 1,
                   UpdatedAtUtc = SYSUTCDATETIME()
            OUTPUT inserted.WorkItemId,
                   inserted.RpaCode,
                   inserted.BatchId,
                   inserted.SessionKey,
                   inserted.AttemptCount,
                   inserted.MaxAttempts,
                   inserted.InputJson,
                   inserted.ConfigurationJson,
                   inserted.AttachmentsJson,
                   inserted.LeaseToken
              INTO @Claimed
                 (WorkItemId, RpaCode, BatchId, SessionKey, AttemptCount, MaxAttempts,
                  InputJson, ConfigurationJson, AttachmentsJson, LeaseToken);

            SELECT WorkItemId,
                   RpaCode,
                   BatchId,
                   SessionKey,
                   AttemptCount,
                   MaxAttempts,
                   InputJson,
                   ConfigurationJson,
                   AttachmentsJson,
                   LeaseToken
              FROM @Claimed;
            """;
        await using var command = new SqlCommand(sql, connection, (SqlTransaction)transaction);
        command.Parameters.AddWithValue("@workerId", _options.WorkerId);
        command.Parameters.AddWithValue("@leaseSeconds", _options.LeaseSeconds);
        command.Parameters.AddWithValue("@retryDelaySeconds", _options.RetryDelaySeconds);
        for (var index = 0; index < enabledCodes.Length; index++)
        {
            command.Parameters.AddWithValue($"@rpa{index}", enabledCodes[index]);
        }

        RpaWorkItem? workItem = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                workItem = new RpaWorkItem(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetGuid(9));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return workItem;
    }

    public async Task StartExecutionAsync(
        string executionId,
        RpaWorkItem workItem,
        CancellationToken cancellationToken)
    {
        const string status = "Running";
        var recoveryPolicyJson = RecoveryPolicyJson(workItem.RpaCode);
        var sql = $"""
            INSERT INTO {_executions}
                (ExecutionId, WorkItemId, WorkerId, LeaseToken, Status, StartedAtUtc,
                 RecoveryPolicyJson)
            SELECT @executionId, WorkItemId, @workerId, @leaseToken, @status,
                   SYSUTCDATETIME(), @recoveryPolicyJson
              FROM {_workItems}
             WHERE WorkItemId = @workItemId
               AND Status = N'Running'
               AND LeaseOwner = @workerId
               AND LeaseToken = @leaseToken
               AND LeaseExpiresAtUtc >= SYSUTCDATETIME();
            """;
        var affected = await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("@executionId", executionId);
                command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                command.Parameters.AddWithValue("@workerId", _options.WorkerId);
                command.Parameters.AddWithValue("@leaseToken", workItem.LeaseToken);
                command.Parameters.AddWithValue("@status", status);
                command.Parameters.Add("@recoveryPolicyJson", SqlDbType.NVarChar, -1).Value =
                    recoveryPolicyJson;
            },
            cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"O lease do item {workItem.WorkItemId} não pertence mais a esta execução.");
        }
    }

    public async Task SetExecutionPackageAsync(
        string executionId,
        RpaWorkItem workItem,
        string originKind,
        RpaPackageSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originKind);
        ArgumentNullException.ThrowIfNull(snapshot);
        var sql = $"""
            UPDATE {_executions}
               SET RpaPackageOrigin = @origin,
                   RpaPackageRevision = @revision,
                   RpaPackageHash = @hash
             WHERE ExecutionId = @executionId
               AND WorkItemId = @workItemId
               AND LeaseToken = @leaseToken
               AND Status = N'Running'
               AND EXISTS
               (
                   SELECT 1
                   FROM {_workItems} AS workItem
                   WHERE workItem.WorkItemId = @workItemId
                     AND workItem.Status = N'Running'
                     AND workItem.LeaseOwner = @workerId
                     AND workItem.LeaseToken = @leaseToken
                     AND workItem.LeaseExpiresAtUtc >= SYSUTCDATETIME()
               );
            """;
        var affected = await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.Add("@origin", SqlDbType.NVarChar, 100).Value =
                    originKind;
                command.Parameters.Add("@revision", SqlDbType.Char, 64).Value =
                    snapshot.Revision.Value;
                command.Parameters.Add("@hash", SqlDbType.Char, 64).Value =
                    snapshot.ContentHash;
                command.Parameters.AddWithValue("@executionId", executionId);
                command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                command.Parameters.AddWithValue("@workerId", _options.WorkerId);
                command.Parameters.AddWithValue("@leaseToken", workItem.LeaseToken);
            },
            cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Não foi possível registrar a revisão do pacote na execução '{executionId}'.");
        }
    }

    public async Task RenewLeaseAsync(
        RpaWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE {_workItems}
               SET LeaseExpiresAtUtc = DATEADD(SECOND, @leaseSeconds, SYSUTCDATETIME()),
                   UpdatedAtUtc = SYSUTCDATETIME()
             WHERE WorkItemId = @workItemId
               AND Status = N'Running'
               AND LeaseOwner = @workerId
               AND LeaseToken = @leaseToken
               AND LeaseExpiresAtUtc >= SYSUTCDATETIME();
            """;
        var affected = await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("@leaseSeconds", _options.LeaseSeconds);
                command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                command.Parameters.AddWithValue("@workerId", _options.WorkerId);
                command.Parameters.AddWithValue("@leaseToken", workItem.LeaseToken);
            },
            cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Não foi possível renovar o lease do item {workItem.WorkItemId}.");
        }
    }

    public async Task CompleteAsync(
        string executionId,
        RpaWorkItem workItem,
        string status,
        string outputJson,
        int executedActions,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE {_workItems}
               SET Status = @status,
                   OutputJson = @outputJson,
                   LeaseOwner = NULL,
                   LeaseToken = NULL,
                   LeaseExpiresAtUtc = NULL,
                   CompletedAtUtc = SYSUTCDATETIME(),
                   UpdatedAtUtc = SYSUTCDATETIME()
             WHERE WorkItemId = @workItemId
               AND LeaseOwner = @workerId
               AND LeaseToken = @leaseToken
               AND LeaseExpiresAtUtc >= SYSUTCDATETIME();

            IF @@ROWCOUNT <> 1
                THROW 51000, 'O item não pertence mais a este worker.', 1;

            UPDATE {_executions}
               SET Status = @status,
                   OutputJson = @outputJson,
                   ExecutedActions = @executedActions,
                   CompletedAtUtc = SYSUTCDATETIME()
             WHERE ExecutionId = @executionId;

            COMMIT TRANSACTION;
            """;
        await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("@status", status);
                command.Parameters.AddWithValue("@outputJson", outputJson);
                command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                command.Parameters.AddWithValue("@workerId", _options.WorkerId);
                command.Parameters.AddWithValue("@leaseToken", workItem.LeaseToken);
                command.Parameters.AddWithValue("@executedActions", executedActions);
                command.Parameters.AddWithValue("@executionId", executionId);
            },
            cancellationToken);
    }

    public async Task FailAsync(
        string executionId,
        RpaWorkItem workItem,
        WorkerFailureDecision decision,
        CancellationToken cancellationToken)
    {
        var shouldRetry = decision.Retry &&
            (decision.PreserveAttempt || workItem.AttemptCount < workItem.MaxAttempts);
        var status = shouldRetry ? "Retry" : "Failed";
        var errorType = Limit(decision.ErrorCode, 200);
        var errorMessage = Limit(decision.Message, 2000);
        var sql = $"""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE {_workItems}
               SET Status = @status,
                   AvailableAtUtc = CASE
                       WHEN @shouldRetry = 1
                       THEN DATEADD(SECOND, @retryDelaySeconds, SYSUTCDATETIME())
                       ELSE AvailableAtUtc
                   END,
                   LeaseOwner = NULL,
                   LeaseToken = NULL,
                   LeaseExpiresAtUtc = NULL,
                   AttemptCount = CASE
                       WHEN @preserveAttempt = 1 AND AttemptCount > 0 THEN AttemptCount - 1
                       ELSE AttemptCount END,
                   ErrorType = @errorType,
                   ErrorMessage = @errorMessage,
                   UpdatedAtUtc = SYSUTCDATETIME()
             WHERE WorkItemId = @workItemId
               AND LeaseOwner = @workerId
               AND LeaseToken = @leaseToken
               AND LeaseExpiresAtUtc >= SYSUTCDATETIME();

            IF @@ROWCOUNT <> 1
                THROW 51000, 'O item não pertence mais a este worker.', 1;

            UPDATE {_executions}
               SET Status = N'Failed',
                   ErrorType = @errorType,
                   ErrorMessage = @errorMessage,
                   CompletedAtUtc = SYSUTCDATETIME()
             WHERE ExecutionId = @executionId;

            COMMIT TRANSACTION;
            """;
        await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("@status", status);
                command.Parameters.AddWithValue("@shouldRetry", shouldRetry);
                command.Parameters.AddWithValue("@preserveAttempt", decision.PreserveAttempt);
                command.Parameters.AddWithValue("@retryDelaySeconds", _options.RetryDelaySeconds);
                command.Parameters.AddWithValue("@errorType", errorType);
                command.Parameters.AddWithValue("@errorMessage", errorMessage);
                command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                command.Parameters.AddWithValue("@workerId", _options.WorkerId);
                command.Parameters.AddWithValue("@leaseToken", workItem.LeaseToken);
                command.Parameters.AddWithValue("@executionId", executionId);
            },
            cancellationToken);
    }

    public async Task ValidateSchemaAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT MissingName
            FROM (VALUES
                (N'{_workItems}', CASE WHEN OBJECT_ID(N'{_workItems}', N'U') IS NULL THEN 1 ELSE 0 END),
                (N'{_executions}', CASE WHEN OBJECT_ID(N'{_executions}', N'U') IS NULL THEN 1 ELSE 0 END),
                (N'{_outputs}', CASE WHEN OBJECT_ID(N'{_outputs}', N'U') IS NULL THEN 1 ELSE 0 END),
                (N'{_artifacts}', CASE WHEN OBJECT_ID(N'{_artifacts}', N'U') IS NULL THEN 1 ELSE 0 END),
                (N'{_events}', CASE WHEN OBJECT_ID(N'{_events}', N'U') IS NULL THEN 1 ELSE 0 END),
                (N'{_workers}', CASE WHEN OBJECT_ID(N'{_workers}', N'U') IS NULL THEN 1 ELSE 0 END),
                (N'{_workItems}.LeaseToken', CASE WHEN COL_LENGTH(N'{_workItems}', N'LeaseToken') IS NULL THEN 1 ELSE 0 END),
                (N'{_executions}.LeaseToken', CASE WHEN COL_LENGTH(N'{_executions}', N'LeaseToken') IS NULL THEN 1 ELSE 0 END),
                (N'{_executions}.RecoveryPolicyJson', CASE WHEN COL_LENGTH(N'{_executions}', N'RecoveryPolicyJson') IS NULL THEN 1 ELSE 0 END),
                (N'{_executions}.RpaPackageOrigin', CASE WHEN COL_LENGTH(N'{_executions}', N'RpaPackageOrigin') IS NULL THEN 1 ELSE 0 END),
                (N'{_executions}.RpaPackageRevision', CASE WHEN COL_LENGTH(N'{_executions}', N'RpaPackageRevision') IS NULL THEN 1 ELSE 0 END),
                (N'{_executions}.RpaPackageHash', CASE WHEN COL_LENGTH(N'{_executions}', N'RpaPackageHash') IS NULL THEN 1 ELSE 0 END),
                (N'{_events}.RpaId', CASE WHEN COL_LENGTH(N'{_events}', N'RpaId') IS NULL THEN 1 ELSE 0 END),
                (N'{_events}.PackageOrigin', CASE WHEN COL_LENGTH(N'{_events}', N'PackageOrigin') IS NULL THEN 1 ELSE 0 END),
                (N'{_events}.PackageRevision', CASE WHEN COL_LENGTH(N'{_events}', N'PackageRevision') IS NULL THEN 1 ELSE 0 END),
                (N'{_events}.PackageHash', CASE WHEN COL_LENGTH(N'{_events}', N'PackageHash') IS NULL THEN 1 ELSE 0 END),
                (N'{_events}.LocatorId', CASE WHEN COL_LENGTH(N'{_events}', N'LocatorId') IS NULL THEN 1 ELSE 0 END),
                (N'{_events}.CandidateId', CASE WHEN COL_LENGTH(N'{_events}', N'CandidateId') IS NULL THEN 1 ELSE 0 END),
                (N'{_events}.ResolutionReason', CASE WHEN COL_LENGTH(N'{_events}', N'ResolutionReason') IS NULL THEN 1 ELSE 0 END),
                (N'{_events}.Detail', CASE WHEN COL_LENGTH(N'{_events}', N'Detail') IS NULL THEN 1 ELSE 0 END),
                (N'{_executions}.LeaseToken NOT NULL', CASE WHEN EXISTS
                    (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{_executions}')
                     AND name = N'LeaseToken' AND is_nullable = 1) THEN 1 ELSE 0 END),
                (N'{_executions}.RecoveryPolicyJson NOT NULL', CASE WHEN EXISTS
                    (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{_executions}')
                     AND name = N'RecoveryPolicyJson' AND is_nullable = 1) THEN 1 ELSE 0 END),
                (N'{_workItems}.CK_Rpa_WorkItem_LeaseIdentity', CASE WHEN NOT EXISTS
                    (SELECT 1 FROM sys.check_constraints
                     WHERE parent_object_id = OBJECT_ID(N'{_workItems}')
                       AND name = N'CK_Rpa_WorkItem_LeaseIdentity'
                       AND is_disabled = 0 AND is_not_trusted = 0) THEN 1 ELSE 0 END),
                (N'{_executions}.CK_Rpa_Execution_PackageIdentity', CASE WHEN NOT EXISTS
                    (SELECT 1 FROM sys.check_constraints
                     WHERE parent_object_id = OBJECT_ID(N'{_executions}')
                       AND name = N'CK_Rpa_Execution_PackageIdentity'
                       AND is_disabled = 0 AND is_not_trusted = 0) THEN 1 ELSE 0 END),
                (N'{_executions}.CK_Rpa_Execution_RecoveryPolicyJson', CASE WHEN NOT EXISTS
                    (SELECT 1 FROM sys.check_constraints
                     WHERE parent_object_id = OBJECT_ID(N'{_executions}')
                       AND name = N'CK_Rpa_Execution_RecoveryPolicyJson'
                       AND is_disabled = 0 AND is_not_trusted = 0) THEN 1 ELSE 0 END)
            ) required(MissingName, Missing)
            WHERE Missing = 1;
            """;
        var missing = new List<string>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) missing.Add(reader.GetString(0));
        if (missing.Count > 0)
            throw new InvalidOperationException(
                "O banco não possui as tabelas exigidas pelo worker: " + string.Join(", ", missing) + ".");
    }

    public Task RecordWorkerHeartbeatAsync(
        WorkerOperationalHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            MERGE {_workers} WITH (HOLDLOCK) AS target
            USING (SELECT @instanceId AS InstanceId) AS source
               ON target.InstanceId = source.InstanceId
            WHEN MATCHED THEN UPDATE SET
                WorkerId=@workerId, HostName=@hostName, ProcessId=@processId,
                Status=@status, Ready=@ready, AcceptingClaims=@acceptingClaims,
                ExecutionEnabled=@executionEnabled, LeadershipAcquired=@leadershipAcquired,
                PollingHealthy=@pollingHealthy, ActiveExecutions=@activeExecutions,
                MaximumParallelism=@maximumParallelism, AvailableExecutionSlots=@availableSlots,
                LeadershipHeartbeatAtUtc=@leadershipHeartbeat, PollingHeartbeatAtUtc=@pollingHeartbeat,
                LastPollingSuccessAtUtc=@lastPollingSuccess, NextPollingAtUtc=@nextPolling,
                LastFailureAtUtc=@lastFailure, LastFailureType=@lastFailureType,
                Finalized=@finalized, FinalizedAtUtc=CASE WHEN @finalized=1 THEN SYSUTCDATETIME() ELSE NULL END,
                UpdatedAtUtc=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (InstanceId, WorkerId, HostName, ProcessId, Status, Ready, AcceptingClaims,
                 ExecutionEnabled, LeadershipAcquired, PollingHealthy, ActiveExecutions,
                 MaximumParallelism, AvailableExecutionSlots, StartedAtUtc,
                 LeadershipHeartbeatAtUtc, PollingHeartbeatAtUtc, LastPollingSuccessAtUtc,
                 NextPollingAtUtc, LastFailureAtUtc, LastFailureType, Finalized,
                 FinalizedAtUtc, UpdatedAtUtc)
            VALUES
                (@instanceId,@workerId,@hostName,@processId,@status,@ready,@acceptingClaims,
                 @executionEnabled,@leadershipAcquired,@pollingHealthy,@activeExecutions,
                 @maximumParallelism,@availableSlots,@startedAt,@leadershipHeartbeat,
                 @pollingHeartbeat,@lastPollingSuccess,@nextPolling,@lastFailure,@lastFailureType,
                 @finalized,CASE WHEN @finalized=1 THEN SYSUTCDATETIME() ELSE NULL END,SYSUTCDATETIME());
            """;
        return ExecuteAsync(sql, command =>
        {
            command.Parameters.AddWithValue("@instanceId", heartbeat.InstanceId);
            command.Parameters.AddWithValue("@workerId", heartbeat.WorkerId);
            command.Parameters.AddWithValue("@hostName", heartbeat.HostName);
            command.Parameters.AddWithValue("@processId", heartbeat.ProcessId);
            command.Parameters.AddWithValue("@status", heartbeat.Status);
            command.Parameters.AddWithValue("@ready", heartbeat.Ready);
            command.Parameters.AddWithValue("@acceptingClaims", heartbeat.AcceptingClaims);
            command.Parameters.AddWithValue("@executionEnabled", heartbeat.ExecutionEnabled);
            command.Parameters.AddWithValue("@leadershipAcquired", heartbeat.LeadershipAcquired);
            command.Parameters.AddWithValue("@pollingHealthy", heartbeat.PollingHealthy);
            command.Parameters.AddWithValue("@activeExecutions", heartbeat.ActiveExecutions);
            command.Parameters.AddWithValue("@maximumParallelism", heartbeat.MaximumParallelism);
            command.Parameters.AddWithValue("@availableSlots", heartbeat.AvailableExecutionSlots);
            command.Parameters.AddWithValue("@startedAt", heartbeat.StartedAtUtc);
            command.Parameters.AddWithValue("@leadershipHeartbeat", DbValue(heartbeat.LeadershipHeartbeatAtUtc));
            command.Parameters.AddWithValue("@pollingHeartbeat", DbValue(heartbeat.PollingHeartbeatAtUtc));
            command.Parameters.AddWithValue("@lastPollingSuccess", DbValue(heartbeat.LastPollingSuccessAtUtc));
            command.Parameters.AddWithValue("@nextPolling", DbValue(heartbeat.NextPollingAtUtc));
            command.Parameters.AddWithValue("@lastFailure", DbValue(heartbeat.LastFailureAtUtc));
            command.Parameters.AddWithValue("@lastFailureType", DbValue(heartbeat.LastFailureType));
            command.Parameters.AddWithValue("@finalized", heartbeat.Finalized);
        }, cancellationToken);
    }

    public async Task SaveOutputsAsync(
        string executionId,
        RpaWorkItem workItem,
        IReadOnlyList<MaterializedOutput> outputs,
        CancellationToken cancellationToken)
    {
        foreach (var output in outputs)
        {
            var sql = $"""
                INSERT INTO {_outputs}
                    (ExecutionId, WorkItemId, Name, JsonValue, Sensitive, CreatedAtUtc)
                SELECT @executionId, WorkItemId, @name, @jsonValue, @sensitive, SYSUTCDATETIME()
                  FROM {_workItems}
                 WHERE WorkItemId = @workItemId
                   AND Status = N'Running'
                   AND LeaseOwner = @workerId
                   AND LeaseToken = @leaseToken
                   AND LeaseExpiresAtUtc >= SYSUTCDATETIME();
                """;
            var affected = await ExecuteAsync(
                sql,
                command =>
                {
                    command.Parameters.AddWithValue("@executionId", executionId);
                    command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                    command.Parameters.AddWithValue("@workerId", _options.WorkerId);
                    command.Parameters.AddWithValue("@leaseToken", workItem.LeaseToken);
                    command.Parameters.AddWithValue("@name", output.Name);
                    command.Parameters.AddWithValue(
                        "@jsonValue",
                        output.Value?.ToJsonString() ?? "null");
                    command.Parameters.AddWithValue("@sensitive", output.Sensitive);
                },
                cancellationToken);
            if (affected != 1)
            {
                throw new InvalidOperationException(
                    $"O lease do item {workItem.WorkItemId} expirou antes de salvar outputs.");
            }
        }
    }

    public async Task SaveArtifactsAsync(
        string executionId,
        RpaWorkItem workItem,
        IReadOnlyList<MaterializedArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        foreach (var artifact in artifacts)
        {
            var sql = $"""
                INSERT INTO {_artifacts}
                    (ExecutionId, WorkItemId, Name, Kind, Path, SizeBytes, Sha256, CreatedAtUtc)
                SELECT @executionId, WorkItemId, @name, @kind, @path, @sizeBytes, @sha256, SYSUTCDATETIME()
                  FROM {_workItems}
                 WHERE WorkItemId = @workItemId
                   AND Status = N'Running'
                   AND LeaseOwner = @workerId
                   AND LeaseToken = @leaseToken
                   AND LeaseExpiresAtUtc >= SYSUTCDATETIME();
                """;
            var affected = await ExecuteAsync(
                sql,
                command =>
                {
                    command.Parameters.AddWithValue("@executionId", executionId);
                    command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                    command.Parameters.AddWithValue("@workerId", _options.WorkerId);
                    command.Parameters.AddWithValue("@leaseToken", workItem.LeaseToken);
                    command.Parameters.AddWithValue("@name", artifact.Name);
                    command.Parameters.AddWithValue("@kind", artifact.Kind);
                    command.Parameters.AddWithValue("@path", artifact.Path);
                    command.Parameters.AddWithValue("@sizeBytes", artifact.SizeBytes);
                    command.Parameters.AddWithValue("@sha256", artifact.Sha256);
                },
                cancellationToken);
            if (affected != 1)
            {
                throw new InvalidOperationException(
                    $"O lease do item {workItem.WorkItemId} expirou antes de salvar artefatos.");
            }
        }
    }

    public async Task AppendEventAsync(
        FlowExecutionEvent executionEvent,
        RpaWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            INSERT INTO {_events}
                (ExecutionId, WorkItemId, Kind, ActionId, ActionName, ActionType,
                 ExecutedActions, ElapsedMilliseconds, FailureCategory, Retryable,
                 OccurredAtUtc, RpaId, PackageOrigin, PackageRevision, PackageHash,
                 LocatorId, CandidateId, ResolutionReason, Detail)
            SELECT
                 @executionId, @workItemId, @kind, @actionId, @actionName, @actionType,
                 @executedActions, @elapsedMilliseconds, @failureCategory, @retryable,
                 @occurredAtUtc, @rpaId, @packageOrigin, @packageRevision, @packageHash,
                 @locatorId, @candidateId, @resolutionReason, @detail
              FROM {_executions} AS execution
             WHERE execution.ExecutionId = @executionId
               AND execution.WorkItemId = @ownedWorkItemId
               AND execution.LeaseToken = @leaseToken
               AND
               (
                   (
                       execution.Status = N'Running'
                       AND EXISTS
                       (
                           SELECT 1
                           FROM {_workItems} AS workItem
                           WHERE workItem.WorkItemId = @ownedWorkItemId
                             AND workItem.Status = N'Running'
                             AND workItem.LeaseOwner = @workerId
                             AND workItem.LeaseToken = @leaseToken
                             AND workItem.LeaseExpiresAtUtc >= SYSUTCDATETIME()
                       )
                   )
                   OR
                   (
                       execution.Status IN (N'Succeeded', N'Validated')
                       AND @kind IN
                       (
                           N'locatorPromotionCompleted',
                           N'locatorPromotionDiscarded',
                           N'locatorPromotionConflict',
                           N'locatorPromotionFailed',
                           N'locatorLearningCompleted'
                       )
                   )
               );
            """;
        var affected = await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("@executionId", executionEvent.ExecutionId);
                command.Parameters.AddWithValue(
                    "@workItemId",
                    DbValue(executionEvent.WorkItemId));
                command.Parameters.AddWithValue("@ownedWorkItemId", workItem.WorkItemId);
                command.Parameters.AddWithValue("@workerId", _options.WorkerId);
                command.Parameters.AddWithValue("@leaseToken", workItem.LeaseToken);
                command.Parameters.AddWithValue("@kind", executionEvent.Kind);
                command.Parameters.AddWithValue("@actionId", DbValue(executionEvent.ActionId));
                command.Parameters.AddWithValue("@actionName", DbValue(executionEvent.ActionName));
                command.Parameters.AddWithValue("@actionType", DbValue(executionEvent.ActionType));
                command.Parameters.AddWithValue(
                    "@executedActions",
                    DbValue(executionEvent.ExecutedActions));
                command.Parameters.AddWithValue(
                    "@elapsedMilliseconds",
                    DbValue(executionEvent.ElapsedMilliseconds));
                command.Parameters.AddWithValue(
                    "@failureCategory",
                    DbValue(executionEvent.FailureCategory?.ToString()));
                command.Parameters.AddWithValue("@retryable", DbValue(executionEvent.Retryable));
                command.Parameters.AddWithValue("@occurredAtUtc", executionEvent.OccurredAtUtc);
                command.Parameters.AddWithValue("@rpaId", DbValue(executionEvent.RpaId));
                command.Parameters.AddWithValue(
                    "@packageOrigin",
                    DbValue(executionEvent.PackageOrigin));
                command.Parameters.AddWithValue(
                    "@packageRevision",
                    DbValue(executionEvent.PackageRevision));
                command.Parameters.AddWithValue(
                    "@packageHash",
                    DbValue(executionEvent.PackageHash));
                command.Parameters.AddWithValue("@locatorId", DbValue(executionEvent.LocatorId));
                command.Parameters.AddWithValue(
                    "@candidateId",
                    DbValue(executionEvent.CandidateId));
                command.Parameters.AddWithValue(
                    "@resolutionReason",
                    DbValue(executionEvent.ResolutionReason));
                command.Parameters.AddWithValue("@detail", DbValue(executionEvent.Detail));
            },
            cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"O lease do item {workItem.WorkItemId} não permite registrar o evento " +
                $"'{executionEvent.Kind}'.");
        }
    }

    private async Task<int> ExecuteAsync(
        string sql,
        Action<SqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        configure(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private string RecoveryPolicyJson(string rpaCode)
    {
        if (!_options.Definitions.TryGetValue(rpaCode, out var definition) ||
            !definition.Enabled ||
            !definition.ClaimEnabled)
        {
            return JsonSerializer.Serialize(new { policyKnown = false });
        }

        static string[] Normalize(IEnumerable<string> values) =>
            values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

        return JsonSerializer.Serialize(new
        {
            policyKnown = true,
            authenticationAttemptActionIds = Normalize(definition.AuthenticationAttemptActionIds),
            mfaAttemptActionIds = Normalize(definition.MfaAttemptActionIds),
            irreversibleActionIds = Normalize(definition.IrreversibleActionIds)
        });
    }

    private static string Quote(string schema, string table) =>
        $"[{schema}].[{table}]";

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}
