using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RpaFlow.Contracts.V2;
using RpaFlow.Packages;
using RpaFlow.Packages.SqlServer;

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var providedConnection = Environment.GetEnvironmentVariable(
    "RPABLOCKLY_SQLSERVER_TEST_CONNECTION");
var requireSql = string.Equals(
    Environment.GetEnvironmentVariable("RPABLOCKLY_REQUIRE_SQL_TESTS"),
    "true",
    StringComparison.OrdinalIgnoreCase);
string? containerName = null;
string? connectionString = providedConnection;
try
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        var allowDocker = string.Equals(
                Environment.GetEnvironmentVariable("CI"),
                "true",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                Environment.GetEnvironmentVariable("RPABLOCKLY_RUN_SQL_DOCKER"),
                "true",
                StringComparison.OrdinalIgnoreCase);
        if (!allowDocker)
        {
            if (requireSql)
            {
                throw new InvalidOperationException(
                    "O teste SQL foi exigido, mas nenhuma conexão foi fornecida e o Docker não foi autorizado.");
            }

            Console.WriteLine("SKIP: integração SQL requer conexão explícita ou " +
                "RPABLOCKLY_RUN_SQL_DOCKER=true.");
            return;
        }

        var docker = await TryStartSqlContainerAsync();
        if (docker is null)
        {
            if (requireSql)
            {
                throw new InvalidOperationException(
                    "O teste SQL é obrigatório, mas Docker/SQL Server não está disponível.");
            }

            Console.WriteLine("SKIP: SQL Server não disponível; defina " +
                "RPABLOCKLY_SQLSERVER_TEST_CONNECTION ou execute com Docker.");
            return;
        }

        containerName = docker.Value.ContainerName;
        connectionString = docker.Value.ConnectionString;
    }

    await WaitForSqlAsync(connectionString!);
    var schema = "rpatest_" + Guid.NewGuid().ToString("N")[..12];
    await ApplyMigrationAsync(repositoryRoot, connectionString!, schema);
    try
    {
        var store = new SqlServerRpaPackageStore(new SqlServerPackageStoreOptions(
            connectionString!,
            schema,
            OriginLocation: "integration-test"));
        var first = await store.PublishAsync(
            "same-rpa",
            Documents("Versão 1"),
            null,
            CancellationToken.None);
        var pinnedTask = store.LoadAsync(
            "same-rpa",
            first.Revision,
            CancellationToken.None);
        var second = await store.PublishAsync(
            "same-rpa",
            Documents("Versão 2"),
            first.Revision,
            CancellationToken.None);
        var pinned = await pinnedTask;
        var current = await store.LoadAsync("same-rpa", null, CancellationToken.None);
        Check(pinned.Revision == first.Revision && current.Revision == second.Revision,
            "execuções distintas mantêm revisões fixadas sem lockstep");

        var contenders = await Task.WhenAll(
            TryPublishAsync(store, "same-rpa", Documents("Concorrente A"), second.Revision),
            TryPublishAsync(store, "same-rpa", Documents("Concorrente B"), second.Revision));
        Check(contenders.Count(value => value) == 1,
            "compare-and-swap SQL possui um único vencedor concorrente");

        var independent = await Task.WhenAll(
            store.PublishAsync("rpa-a", Documents("A"), null, CancellationToken.None),
            store.PublishAsync("rpa-b", Documents("B"), null, CancellationToken.None));
        Check(independent[0].Revision != independent[1].Revision,
            "RPAs diferentes publicam de forma independente");
        Check((await store.ListRevisionsAsync("same-rpa", CancellationToken.None)).Count == 3,
            "histórico SQL preserva todas as revisões publicadas");
    }
    finally
    {
        await DropSchemaAsync(connectionString!, schema);
    }
}
finally
{
    if (containerName is not null)
    {
        _ = await RunProcessAsync(
            "docker",
            ["rm", "--force", containerName],
            throwOnError: false);
    }
}

Console.WriteLine("Package store SQL Server validado com sucesso.");

static RpaPackageDocuments Documents(string name) => new(
    new FlowDefinition
    {
        Name = name,
        Actions =
        [
            new FlowActionDefinition
            {
                Id = "start",
                Type = "setVariable",
                Name = "Iniciar",
                Value = System.Text.Json.JsonSerializer.SerializeToElement("ok"),
                Output = "runtime.status"
            }
        ]
    },
    new LocatorCatalog(),
    new RpaPolicyDefinition());

static async Task<bool> TryPublishAsync(
    IRpaPackageWriter store,
    string rpaId,
    RpaPackageDocuments documents,
    PackageRevision expected)
{
    try
    {
        await store.PublishAsync(rpaId, documents, expected, CancellationToken.None);
        return true;
    }
    catch (PackageRevisionConflictException)
    {
        return false;
    }
    catch (SqlException exception) when (exception.Number == 1205)
    {
        return false;
    }
}

static async Task<(string ContainerName, string ConnectionString)?>
    TryStartSqlContainerAsync()
{
    var version = await RunProcessAsync("docker", ["version"], throwOnError: false);
    if (version.ExitCode != 0)
    {
        return null;
    }

    var name = "rpablockly-sql-" + Guid.NewGuid().ToString("N")[..12];
    var password = "T3st!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
    var started = await RunProcessAsync(
        "docker",
        [
            "run", "--detach", "--rm", "--name", name,
            "--env", "ACCEPT_EULA=Y",
            "--env", "MSSQL_SA_PASSWORD=" + password,
            "--publish", "127.0.0.1::1433",
            "mcr.microsoft.com/mssql/server:2022-latest"
        ],
        throwOnError: false);
    if (started.ExitCode != 0)
    {
        return null;
    }

    try
    {
        var portResult = await RunProcessAsync(
            "docker",
            ["port", name, "1433/tcp"],
            throwOnError: true);
        var match = Regex.Match(portResult.Output, @":(?<port>[0-9]+)\s*$");
        if (!match.Success)
        {
            throw new InvalidOperationException("Docker não informou a porta SQL publicada.");
        }

        return (
            name,
            $"Server=127.0.0.1,{match.Groups["port"].Value};Database=master;" +
            $"User ID=sa;Password={password};Encrypt=True;TrustServerCertificate=True;");
    }
    catch
    {
        _ = await RunProcessAsync("docker", ["rm", "--force", name], false);
        throw;
    }
}

static async Task WaitForSqlAsync(string connectionString)
{
    Exception? last = null;
    for (var attempt = 0; attempt < 90; attempt++)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return;
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            last = exception;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    throw new InvalidOperationException("SQL Server não ficou pronto no prazo.", last);
}

static async Task ApplyMigrationAsync(string root, string connectionString, string schema)
{
    var path = Path.Combine(root, "database", "sqlserver", "003_create_rpa_package_store.sql");
    var script = new UTF8Encoding(false, true).GetString(await File.ReadAllBytesAsync(path));
    script = Regex.Replace(
        script,
        "^:setvar[^\\r\\n]*(?:\\r?\\n)?",
        string.Empty,
        RegexOptions.Multiline | RegexOptions.CultureInvariant)
        .Replace("$(RpaSchema)", schema, StringComparison.Ordinal);
    await ExecuteAsync(connectionString, script);
}

static Task DropSchemaAsync(string connectionString, string schema) => ExecuteAsync(
    connectionString,
    $"DROP TABLE [{schema}].[RpaPackageCurrent];" +
    $"DROP TABLE [{schema}].[RpaPackageDocument];" +
    $"DROP TABLE [{schema}].[RpaPackageRevision];" +
    $"DROP SCHEMA [{schema}];");

static async Task ExecuteAsync(string connectionString, string sql)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
    _ = await command.ExecuteNonQueryAsync();
}

static async Task<(int ExitCode, string Output)> RunProcessAsync(
    string fileName,
    IReadOnlyList<string> arguments,
    bool throwOnError)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    foreach (var argument in arguments)
    {
        process.StartInfo.ArgumentList.Add(argument);
    }

    try
    {
        process.Start();
    }
    catch when (!throwOnError)
    {
        return (-1, string.Empty);
    }

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var output = (await outputTask) + (await errorTask);
    if (throwOnError && process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"O processo '{fileName}' terminou com código {process.ExitCode}.");
    }

    return (process.ExitCode, output.Trim());
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

    throw new DirectoryNotFoundException("A raiz do repositório não foi encontrada.");
}

static void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Falha: {description}.");
    }

    Console.WriteLine($"OK: {description}.");
}
