using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using RpaFlow.Contracts.V2;

namespace RpaFlow.Packages.SqlServer;

public sealed class SqlServerRpaPackageStore : IRpaPackageStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly SqlServerPackageStoreOptions _options;
    private readonly string _revisions;
    private readonly string _documents;
    private readonly string _current;

    public SqlServerRpaPackageStore(SqlServerPackageStoreOptions options)
    {
        SqlServerPackageStoreOptionsValidator.Validate(options);
        _options = options;
        _revisions = Quote(options.Schema, "RpaPackageRevision");
        _documents = Quote(options.Schema, "RpaPackageDocument");
        _current = Quote(options.Schema, "RpaPackageCurrent");
    }

    public async Task<RpaPackageSnapshot> LoadAsync(
        string rpaId,
        PackageRevision? revision,
        CancellationToken cancellationToken)
    {
        RpaPackageStoreRules.ValidateRpaId(rpaId);
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var selected = revision ?? await ReadCurrentRevisionAsync(
            connection,
            transaction,
            rpaId,
            lockForUpdate: false,
            cancellationToken) ?? throw new KeyNotFoundException(
                $"O pacote SQL '{rpaId}' não possui revisão atual.");
        var metadata = await ReadMetadataAsync(
            connection,
            transaction,
            rpaId,
            selected,
            cancellationToken);
        var values = await ReadDocumentsAsync(
            connection,
            transaction,
            rpaId,
            selected,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (values.Count != 3 ||
            !values.TryGetValue("flow.production.json", out var flowJson) ||
            !values.TryGetValue("locators.production.json", out var locatorJson) ||
            !values.TryGetValue("rpa.policy.json", out var policyJson))
        {
            throw new InvalidOperationException(
                $"A revisão SQL '{selected}' de '{rpaId}' não contém os três documentos.");
        }

        var package = new RpaPackageDocuments(
            V2JsonSerializer.Deserialize<FlowDefinition>(flowJson, "flow.production.json"),
            V2JsonSerializer.Deserialize<LocatorCatalog>(
                locatorJson,
                "locators.production.json"),
            V2JsonSerializer.Deserialize<RpaPolicyDefinition>(policyJson, "rpa.policy.json"));
        var snapshot = new RpaPackageSnapshot(
            rpaId,
            selected,
            package,
            new RpaPackageOrigin(metadata.OriginKind, metadata.OriginLocation));
        if (!snapshot.ContentHash.Equals(metadata.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"O hash da revisão SQL '{selected}' de '{rpaId}' não confere.");
        }

        return snapshot;
    }

    public async Task<PackageWriteResult> PublishAsync(
        string rpaId,
        RpaPackageDocuments documents,
        PackageRevision? expectedRevision,
        CancellationToken cancellationToken)
    {
        RpaPackageStoreRules.ValidateRpaId(rpaId);
        RpaPackageValidator.Validate(documents);
        var hash = CanonicalJson.ComputePackageHash(documents);
        var revision = new PackageRevision(hash);
        var serialized = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["flow.production.json"] = Decode(CanonicalJson.Serialize(documents.Flow)),
            ["locators.production.json"] = Decode(CanonicalJson.Serialize(documents.Locators)),
            ["rpa.policy.json"] = Decode(CanonicalJson.Serialize(documents.Policy))
        };

        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var current = await ReadCurrentRevisionAsync(
            connection,
            transaction,
            rpaId,
            lockForUpdate: true,
            cancellationToken);
        RpaPackageStoreRules.EnsureExpectedRevision(rpaId, current, expectedRevision);
        var created = !await RevisionExistsAsync(
            connection,
            transaction,
            rpaId,
            revision,
            cancellationToken);
        if (created)
        {
            await InsertRevisionAsync(
                connection,
                transaction,
                rpaId,
                revision,
                hash,
                serialized,
                cancellationToken);
        }

        await UpdateCurrentAsync(
            connection,
            transaction,
            rpaId,
            revision,
            current is not null,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PackageWriteResult(revision, hash, created);
    }

    public async Task<IReadOnlyList<PackageRevision>> ListRevisionsAsync(
        string rpaId,
        CancellationToken cancellationToken)
    {
        RpaPackageStoreRules.ValidateRpaId(rpaId);
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var sql = $"""
            SELECT Revision
              FROM {_revisions}
             WHERE RpaId = @rpaId
             ORDER BY CreatedAtUtc, Revision;
            """;
        await using var command = Command(sql, connection);
        command.Parameters.Add("@rpaId", SqlDbType.NVarChar, 200).Value = rpaId;
        var result = new List<PackageRevision>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PackageRevision(reader.GetString(0)));
        }

        return result;
    }

    private async Task<PackageRevision?> ReadCurrentRevisionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string rpaId,
        bool lockForUpdate,
        CancellationToken cancellationToken)
    {
        var hint = lockForUpdate ? " WITH (UPDLOCK, HOLDLOCK)" : " WITH (HOLDLOCK)";
        var sql = $"SELECT Revision FROM {_current}{hint} WHERE RpaId = @rpaId;";
        await using var command = Command(sql, connection, transaction);
        command.Parameters.Add("@rpaId", SqlDbType.NVarChar, 200).Value = rpaId;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : new PackageRevision((string)value);
    }

    private async Task<RevisionMetadata> ReadMetadataAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string rpaId,
        PackageRevision revision,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT ContentHash, OriginKind, OriginLocation
              FROM {_revisions} WITH (HOLDLOCK)
             WHERE RpaId = @rpaId AND Revision = @revision;
            """;
        await using var command = Command(sql, connection, transaction);
        AddIdentity(command, rpaId, revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new KeyNotFoundException(
                $"A revisão SQL '{revision}' do pacote '{rpaId}' não existe.");
        }

        return new RevisionMetadata(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private async Task<Dictionary<string, string>> ReadDocumentsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string rpaId,
        PackageRevision revision,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT DocumentName, JsonContent, Sha256
              FROM {_documents} WITH (HOLDLOCK)
             WHERE RpaId = @rpaId AND Revision = @revision;
            """;
        await using var command = Command(sql, connection, transaction);
        AddIdentity(command, rpaId, revision);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            var content = reader.GetString(1);
            var expectedHash = reader.GetString(2);
            var actualHash = Hash(StrictUtf8.GetBytes(content));
            if (!actualHash.Equals(expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"O documento SQL '{name}' da revisão '{revision}' está corrompido.");
            }

            if (!result.TryAdd(name, content))
            {
                throw new InvalidOperationException(
                    $"O documento SQL '{name}' está duplicado na revisão '{revision}'.");
            }
        }

        return result;
    }

    private async Task<bool> RevisionExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string rpaId,
        PackageRevision revision,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT COUNT_BIG(1)
              FROM {_revisions} WITH (UPDLOCK, HOLDLOCK)
             WHERE RpaId = @rpaId AND Revision = @revision;
            """;
        await using var command = Command(sql, connection, transaction);
        AddIdentity(command, rpaId, revision);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L) == 1L;
    }

    private async Task InsertRevisionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string rpaId,
        PackageRevision revision,
        string hash,
        IReadOnlyDictionary<string, string> documents,
        CancellationToken cancellationToken)
    {
        var revisionSql = $"""
            INSERT INTO {_revisions}
                (RpaId, Revision, ContentHash, OriginKind, OriginLocation, CreatedAtUtc)
            VALUES
                (@rpaId, @revision, @hash, @originKind, @originLocation,
                 SYSUTCDATETIME());
            """;
        await using (var command = Command(revisionSql, connection, transaction))
        {
            AddIdentity(command, rpaId, revision);
            command.Parameters.Add("@hash", SqlDbType.Char, 64).Value = hash;
            command.Parameters.Add("@originKind", SqlDbType.NVarChar, 100).Value =
                _options.OriginKind;
            command.Parameters.Add("@originLocation", SqlDbType.NVarChar, 1000).Value =
                _options.OriginLocation;
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var documentSql = $"""
            INSERT INTO {_documents}
                (RpaId, Revision, DocumentName, JsonContent, Sha256)
            VALUES
                (@rpaId, @revision, @name, @json, @sha256);
            """;
        foreach (var document in documents.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            await using var command = Command(documentSql, connection, transaction);
            AddIdentity(command, rpaId, revision);
            command.Parameters.Add("@name", SqlDbType.NVarChar, 40).Value = document.Key;
            command.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = document.Value;
            command.Parameters.Add("@sha256", SqlDbType.Char, 64).Value =
                Hash(StrictUtf8.GetBytes(document.Value));
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task UpdateCurrentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string rpaId,
        PackageRevision revision,
        bool exists,
        CancellationToken cancellationToken)
    {
        var sql = exists
            ? $"""
                UPDATE {_current}
                   SET Revision = @revision, UpdatedAtUtc = SYSUTCDATETIME()
                 WHERE RpaId = @rpaId;
                """
            : $"""
                INSERT INTO {_current} (RpaId, Revision, UpdatedAtUtc)
                VALUES (@rpaId, @revision, SYSUTCDATETIME());
                """;
        await using var command = Command(sql, connection, transaction);
        AddIdentity(command, rpaId, revision);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                $"Não foi possível atualizar a revisão atual do pacote '{rpaId}'.");
        }
    }

    private SqlCommand Command(
        string sql,
        SqlConnection connection,
        SqlTransaction? transaction = null) =>
        new(sql, connection, transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };

    private static void AddIdentity(
        SqlCommand command,
        string rpaId,
        PackageRevision revision)
    {
        command.Parameters.Add("@rpaId", SqlDbType.NVarChar, 200).Value = rpaId;
        command.Parameters.Add("@revision", SqlDbType.Char, 64).Value = revision.Value;
    }

    private static string Decode(byte[] value) => StrictUtf8.GetString(value);

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value));

    private static string Quote(string schema, string table) =>
        $"[{schema}].[{table}]";

    private sealed record RevisionMetadata(
        string ContentHash,
        string OriginKind,
        string OriginLocation);
}
