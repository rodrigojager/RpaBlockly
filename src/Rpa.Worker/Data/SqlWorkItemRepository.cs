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
    WorkerEnvironment environment)
{
    private readonly string _connectionString = environment.ConnectionString;
    private readonly RpaWorkerOptions _options = options;
    private readonly string _workItems = Quote(options.Tables.Schema, options.Tables.WorkItems);
    private readonly string _executions = Quote(options.Tables.Schema, options.Tables.Executions);
    private readonly string _outputs = Quote(options.Tables.Schema, options.Tables.Outputs);
    private readonly string _artifacts = Quote(options.Tables.Schema, options.Tables.Artifacts);
    private readonly string _events = Quote(options.Tables.Schema, options.Tables.Events);

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
            ;WITH Candidate AS
            (
                SELECT TOP (1) *
                FROM {_workItems} WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status IN (N'Pending', N'Retry')
                  AND AvailableAtUtc <= SYSUTCDATETIME()
                  AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc < SYSUTCDATETIME())
                  AND RpaCode IN ({codeParameters})
                ORDER BY Priority DESC, CreatedAtUtc, WorkItemId
            )
            UPDATE Candidate
               SET Status = N'Running',
                   LeaseOwner = @workerId,
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
                   inserted.AttachmentsJson;
            """;
        await using var command = new SqlCommand(sql, connection, (SqlTransaction)transaction);
        command.Parameters.AddWithValue("@workerId", _options.WorkerId);
        command.Parameters.AddWithValue("@leaseSeconds", _options.LeaseSeconds);
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
                    reader.GetString(8));
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
        var sql = $"""
            INSERT INTO {_executions}
                (ExecutionId, WorkItemId, WorkerId, Status, StartedAtUtc)
            VALUES
                (@executionId, @workItemId, @workerId, @status, SYSUTCDATETIME());
            """;
        await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("@executionId", executionId);
                command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                command.Parameters.AddWithValue("@workerId", _options.WorkerId);
                command.Parameters.AddWithValue("@status", status);
            },
            cancellationToken);
    }

    public async Task SetExecutionPackageAsync(
        string executionId,
        string originName,
        RpaPackageSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originName);
        ArgumentNullException.ThrowIfNull(snapshot);
        var sql = $"""
            UPDATE {_executions}
               SET RpaPackageOrigin = @origin,
                   RpaPackageRevision = @revision,
                   RpaPackageHash = @hash
             WHERE ExecutionId = @executionId
               AND Status = N'Running';
            """;
        var affected = await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.Add("@origin", SqlDbType.NVarChar, 100).Value =
                    originName;
                command.Parameters.Add("@revision", SqlDbType.Char, 64).Value =
                    snapshot.Revision.Value;
                command.Parameters.Add("@hash", SqlDbType.Char, 64).Value =
                    snapshot.ContentHash;
                command.Parameters.AddWithValue("@executionId", executionId);
            },
            cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Não foi possível registrar a revisão do pacote na execução '{executionId}'.");
        }
    }

    public async Task RenewLeaseAsync(
        Guid workItemId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE {_workItems}
               SET LeaseExpiresAtUtc = DATEADD(SECOND, @leaseSeconds, SYSUTCDATETIME()),
                   UpdatedAtUtc = SYSUTCDATETIME()
             WHERE WorkItemId = @workItemId
               AND Status = N'Running'
               AND LeaseOwner = @workerId;
            """;
        var affected = await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("@leaseSeconds", _options.LeaseSeconds);
                command.Parameters.AddWithValue("@workItemId", workItemId);
                command.Parameters.AddWithValue("@workerId", _options.WorkerId);
            },
            cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Não foi possível renovar o lease do item {workItemId}.");
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
                   LeaseExpiresAtUtc = NULL,
                   CompletedAtUtc = SYSUTCDATETIME(),
                   UpdatedAtUtc = SYSUTCDATETIME()
             WHERE WorkItemId = @workItemId
               AND LeaseOwner = @workerId;

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
                command.Parameters.AddWithValue("@executedActions", executedActions);
                command.Parameters.AddWithValue("@executionId", executionId);
            },
            cancellationToken);
    }

    public async Task FailAsync(
        string executionId,
        RpaWorkItem workItem,
        Exception exception,
        bool allowRetry,
        CancellationToken cancellationToken)
    {
        var shouldRetry = allowRetry && workItem.AttemptCount < workItem.MaxAttempts;
        var status = shouldRetry ? "Retry" : "Failed";
        var errorType = exception.GetType().Name;
        var errorMessage = Limit(exception.Message, 2000);
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
                   LeaseExpiresAtUtc = NULL,
                   ErrorType = @errorType,
                   ErrorMessage = @errorMessage,
                   UpdatedAtUtc = SYSUTCDATETIME()
             WHERE WorkItemId = @workItemId
               AND LeaseOwner = @workerId;

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
                command.Parameters.AddWithValue("@retryDelaySeconds", _options.RetryDelaySeconds);
                command.Parameters.AddWithValue("@errorType", errorType);
                command.Parameters.AddWithValue("@errorMessage", errorMessage);
                command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                command.Parameters.AddWithValue("@workerId", _options.WorkerId);
                command.Parameters.AddWithValue("@executionId", executionId);
            },
            cancellationToken);
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
                VALUES
                    (@executionId, @workItemId, @name, @jsonValue, @sensitive, SYSUTCDATETIME());
                """;
            await ExecuteAsync(
                sql,
                command =>
                {
                    command.Parameters.AddWithValue("@executionId", executionId);
                    command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                    command.Parameters.AddWithValue("@name", output.Name);
                    command.Parameters.AddWithValue(
                        "@jsonValue",
                        output.Value?.ToJsonString() ?? "null");
                    command.Parameters.AddWithValue("@sensitive", output.Sensitive);
                },
                cancellationToken);
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
                VALUES
                    (@executionId, @workItemId, @name, @kind, @path, @sizeBytes, @sha256, SYSUTCDATETIME());
                """;
            await ExecuteAsync(
                sql,
                command =>
                {
                    command.Parameters.AddWithValue("@executionId", executionId);
                    command.Parameters.AddWithValue("@workItemId", workItem.WorkItemId);
                    command.Parameters.AddWithValue("@name", artifact.Name);
                    command.Parameters.AddWithValue("@kind", artifact.Kind);
                    command.Parameters.AddWithValue("@path", artifact.Path);
                    command.Parameters.AddWithValue("@sizeBytes", artifact.SizeBytes);
                    command.Parameters.AddWithValue("@sha256", artifact.Sha256);
                },
                cancellationToken);
        }
    }

    public async Task AppendEventAsync(
        FlowExecutionEvent executionEvent,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            INSERT INTO {_events}
                (ExecutionId, WorkItemId, Kind, ActionId, ActionName, ActionType,
                 ExecutedActions, ElapsedMilliseconds, FailureCategory, Retryable,
                 OccurredAtUtc, RpaId, PackageOrigin, PackageRevision, PackageHash,
                 LocatorId, CandidateId, ResolutionReason, Detail)
            VALUES
                (@executionId, @workItemId, @kind, @actionId, @actionName, @actionType,
                 @executedActions, @elapsedMilliseconds, @failureCategory, @retryable,
                 @occurredAtUtc, @rpaId, @packageOrigin, @packageRevision, @packageHash,
                 @locatorId, @candidateId, @resolutionReason, @detail);
            """;
        await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("@executionId", executionEvent.ExecutionId);
                command.Parameters.AddWithValue(
                    "@workItemId",
                    DbValue(executionEvent.WorkItemId));
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

    private static string Quote(string schema, string table) =>
        $"[{schema}].[{table}]";

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}
