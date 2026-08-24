using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Rpa.Worker.Configuration;

namespace Rpa.Worker.Hosting;

public sealed class SqlWorkerExecutionLease(
    RpaWorkerOptions options,
    WorkerEnvironment environment,
    WorkerStartupValidationState validationState,
    WorkerExecutionLeaseState leaseState,
    WorkerRuntimeState runtimeState,
    ILogger<SqlWorkerExecutionLease> logger) : BackgroundService, IWorkerExecutionLease
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

    public Task<WorkerExecutionLeaseHandle> WaitUntilAcquiredAsync(CancellationToken token) =>
        leaseState.WaitUntilAcquiredAsync(token);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await validationState.WaitUntilValidatedAsync(stoppingToken);
        if (!options.Enabled)
        {
            await WaitUntilStoppedAsync(stoppingToken);
            return;
        }

        var retryDelay = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            SqlConnection? connection = null;
            var acquired = false;
            try
            {
                connection = new SqlConnection(environment.ConnectionString);
                await connection.OpenAsync(stoppingToken);
                if (await AcquireAsync(connection, options.GlobalExecutionLockName, stoppingToken) < 0)
                {
                    throw new InvalidOperationException(
                        "Outra instância mantém a trava global de execução.");
                }

                acquired = true;
                retryDelay = TimeSpan.FromSeconds(5);
                leaseState.MarkAcquired();
                runtimeState.MarkLeadershipAcquired();
                logger.LogInformation("Trava global de execução adquirida.");
                await MonitorAsync(connection, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                runtimeState.MarkLeadershipLost(exception);
                logger.LogError(
                    exception,
                    "A trava global está indisponível; uma nova tentativa ocorrerá em {Delay}s.",
                    retryDelay.TotalSeconds);
            }
            finally
            {
                if (acquired) leaseState.MarkUnavailable();
                if (connection is not null)
                {
                    try
                    {
                        if (acquired && connection.State == ConnectionState.Open)
                            await ReleaseAsync(connection, options.GlobalExecutionLockName);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Não foi possível liberar explicitamente a trava global.");
                        SqlConnection.ClearPool(connection);
                    }
                    await connection.DisposeAsync();
                }
            }

            try
            {
                await Task.Delay(retryDelay, stoppingToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(60, retryDelay.TotalSeconds * 2));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task MonitorAsync(SqlConnection connection, CancellationToken token)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(token))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT APPLOCK_MODE(N'public', @resource, N'Session');";
            command.CommandTimeout = 10;
            command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255)
            {
                Value = options.GlobalExecutionLockName
            });
            var mode = Convert.ToString(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
            if (!string.Equals(mode, "Exclusive", StringComparison.Ordinal))
                throw new InvalidOperationException("A trava global deixou de estar exclusiva.");
            runtimeState.MarkLeadershipHeartbeat();
        }
    }

    private static async Task<int> AcquireAsync(SqlConnection connection, string resource, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive',
                @LockOwner='Session', @LockTimeout=0, @DbPrincipal='public';
            SELECT @result;
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
        return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    private static async Task ReleaseAsync(SqlConnection connection, string resource)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session', @DbPrincipal='public';";
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task WaitUntilStoppedAsync(CancellationToken token)
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
