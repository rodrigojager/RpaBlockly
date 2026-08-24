using System.Text;
using System.Text.Json;
using RpaFlow.Contracts.V2;

namespace RpaFlow.Packages;

public sealed class FileRpaPackageStore : IRpaPackageStore
{
    private const string FlowFileName = "flow.production.json";
    private const string LocatorsFileName = "locators.production.json";
    private const string PolicyFileName = "rpa.policy.json";
    private const string CurrentFileName = "current.json";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly string _root;
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private readonly Action<FilePackageWriteStage>? _faultInjector;

    public FileRpaPackageStore(string rootDirectory)
        : this(rootDirectory, null)
    {
    }

    internal FileRpaPackageStore(
        string rootDirectory,
        Action<FilePackageWriteStage>? faultInjector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        _faultInjector = faultInjector;
        Directory.CreateDirectory(_root);
    }

    public async Task<RpaPackageSnapshot> LoadAsync(
        string rpaId,
        PackageRevision? revision,
        CancellationToken cancellationToken)
    {
        RpaPackageStoreRules.ValidateRpaId(rpaId);
        var rpaDirectory = ResolveRpaDirectory(rpaId);
        var selected = revision ?? await ReadCurrentRevisionAsync(
            rpaDirectory,
            cancellationToken);
        var revisionDirectory = ResolveRevisionDirectory(rpaDirectory, selected);
        if (!Directory.Exists(revisionDirectory))
        {
            throw new KeyNotFoundException(
                $"A revisão '{selected}' do pacote '{rpaId}' não existe.");
        }

        return await LoadFromRevisionDirectoryAsync(
            rpaId,
            selected,
            revisionDirectory,
            cancellationToken);
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
        var rpaDirectory = ResolveRpaDirectory(rpaId);

        await _processLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(rpaDirectory);
            await using var fileLock = await AcquireFileLockAsync(
                rpaDirectory,
                cancellationToken);
            var current = await TryReadCurrentRevisionAsync(
                rpaDirectory,
                cancellationToken);
            RpaPackageStoreRules.EnsureExpectedRevision(
                rpaId,
                current,
                expectedRevision);

            var revisionDirectory = ResolveRevisionDirectory(rpaDirectory, revision);
            var created = !Directory.Exists(revisionDirectory);
            if (created)
            {
                await WriteRevisionAsync(
                    rpaId,
                    revision,
                    documents,
                    rpaDirectory,
                    revisionDirectory,
                    cancellationToken);
            }
            else
            {
                var existing = await LoadFromRevisionDirectoryAsync(
                    rpaId,
                    revision,
                    revisionDirectory,
                    cancellationToken);
                if (!existing.ContentHash.Equals(hash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"A revisão imutável '{revision}' possui conteúdo divergente.");
                }
            }

            await WriteCurrentRevisionAsync(
                rpaDirectory,
                revision,
                cancellationToken);
            return new PackageWriteResult(revision, hash, created);
        }
        finally
        {
            _processLock.Release();
        }
    }

    public Task<IReadOnlyList<PackageRevision>> ListRevisionsAsync(
        string rpaId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RpaPackageStoreRules.ValidateRpaId(rpaId);
        var revisionsDirectory = Path.Combine(
            ResolveRpaDirectory(rpaId),
            "revisions");
        EnsureInsideRoot(revisionsDirectory);
        if (!Directory.Exists(revisionsDirectory))
        {
            return Task.FromResult<IReadOnlyList<PackageRevision>>([]);
        }

        var revisions = Directory.EnumerateDirectories(revisionsDirectory)
            .Select(Path.GetFileName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(value => new PackageRevision(value!))
            .ToArray();
        return Task.FromResult<IReadOnlyList<PackageRevision>>(revisions);
    }

    private async Task WriteRevisionAsync(
        string rpaId,
        PackageRevision revision,
        RpaPackageDocuments documents,
        string rpaDirectory,
        string revisionDirectory,
        CancellationToken cancellationToken)
    {
        var stagingRoot = Path.Combine(rpaDirectory, ".staging");
        var stagingDirectory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        EnsureInsideRoot(stagingDirectory);
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            await WriteDocumentAsync(
                Path.Combine(stagingDirectory, FlowFileName),
                documents.Flow,
                cancellationToken);
            Inject(FilePackageWriteStage.FlowStaged);
            await WriteDocumentAsync(
                Path.Combine(stagingDirectory, LocatorsFileName),
                documents.Locators,
                cancellationToken);
            Inject(FilePackageWriteStage.LocatorsStaged);
            await WriteDocumentAsync(
                Path.Combine(stagingDirectory, PolicyFileName),
                documents.Policy,
                cancellationToken);
            Inject(FilePackageWriteStage.PolicyStaged);

            var verification = await LoadFromRevisionDirectoryAsync(
                rpaId,
                revision,
                stagingDirectory,
                cancellationToken);
            var expectedHash = CanonicalJson.ComputePackageHash(documents);
            if (!verification.ContentHash.Equals(expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A revisão em staging divergiu dos documentos validados.");
            }
            Inject(FilePackageWriteStage.StagingValidated);

            Directory.CreateDirectory(Path.GetDirectoryName(revisionDirectory)!);
            Directory.Move(stagingDirectory, revisionDirectory);
            Inject(FilePackageWriteStage.RevisionPublished);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                EnsureInsideRoot(stagingDirectory);
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private async Task<RpaPackageSnapshot> LoadFromRevisionDirectoryAsync(
        string rpaId,
        PackageRevision revision,
        string directory,
        CancellationToken cancellationToken)
    {
        EnsureInsideRoot(directory);
        var flow = await ReadDocumentAsync<FlowDefinition>(
            Path.Combine(directory, FlowFileName),
            "flow V2",
            cancellationToken);
        var locators = await ReadDocumentAsync<LocatorCatalog>(
            Path.Combine(directory, LocatorsFileName),
            "catálogo de localizadores",
            cancellationToken);
        var policy = await ReadDocumentAsync<RpaPolicyDefinition>(
            Path.Combine(directory, PolicyFileName),
            "política do RPA",
            cancellationToken);
        var documents = new RpaPackageDocuments(flow, locators, policy);
        RpaPackageValidator.Validate(documents);
        return new RpaPackageSnapshot(
            rpaId,
            revision,
            documents,
            new RpaPackageOrigin("file", directory));
    }

    private static async Task<T> ReadDocumentAsync<T>(
        string path,
        string description,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Documento obrigatório ausente: {description}.",
                path);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var json = StrictUtf8.GetString(bytes);
        try
        {
            return V2JsonSerializer.Deserialize<T>(json, description);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"O documento {description} não corresponde ao contrato: " +
                exception.Message,
                exception);
        }
    }

    private static async Task WriteDocumentAsync<T>(
        string path,
        T document,
        CancellationToken cancellationToken)
    {
        var json = V2JsonSerializer.Serialize(document).ReplaceLineEndings("\n") + "\n";
        await File.WriteAllTextAsync(path, json, StrictUtf8, cancellationToken);
    }

    private async Task<PackageRevision> ReadCurrentRevisionAsync(
        string rpaDirectory,
        CancellationToken cancellationToken) =>
        await TryReadCurrentRevisionAsync(rpaDirectory, cancellationToken)
        ?? throw new KeyNotFoundException(
            $"O pacote em '{rpaDirectory}' não possui revisão atual.");

    private static async Task<PackageRevision?> TryReadCurrentRevisionAsync(
        string rpaDirectory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(rpaDirectory, CurrentFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var pointer = JsonSerializer.Deserialize<CurrentRevisionPointer>(
            StrictUtf8.GetString(bytes),
            V2JsonSerializer.ReadOptions)
            ?? throw new InvalidOperationException("current.json está vazio.");
        return new PackageRevision(pointer.Revision);
    }

    private async Task WriteCurrentRevisionAsync(
        string rpaDirectory,
        PackageRevision revision,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(rpaDirectory, CurrentFileName);
        var temporaryPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        var backupPath = path + ".bak";
        try
        {
            var json = JsonSerializer.Serialize(
                new CurrentRevisionPointer(revision.Value),
                V2JsonSerializer.WriteOptions).ReplaceLineEndings("\n") + "\n";
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                StrictUtf8,
                cancellationToken);
            Inject(FilePackageWriteStage.CurrentPointerStaged);
            _ = JsonSerializer.Deserialize<CurrentRevisionPointer>(
                StrictUtf8.GetString(await File.ReadAllBytesAsync(
                    temporaryPath,
                    cancellationToken)),
                V2JsonSerializer.ReadOptions)
                ?? throw new InvalidOperationException("Ponteiro temporário inválido.");
            Inject(FilePackageWriteStage.CurrentPointerValidated);

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void Inject(FilePackageWriteStage stage) => _faultInjector?.Invoke(stage);

    private static async Task<FileStream> AcquireFileLockAsync(
        string rpaDirectory,
        CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(rpaDirectory, ".package.lock");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }

    private string ResolveRpaDirectory(string rpaId)
    {
        var path = Path.GetFullPath(Path.Combine(_root, rpaId));
        EnsureInsideRoot(path);
        return path;
    }

    private string ResolveRevisionDirectory(
        string rpaDirectory,
        PackageRevision revision)
    {
        if (revision.Value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("A revisão de conteúdo deve ser hexadecimal.");
        }

        var path = Path.GetFullPath(Path.Combine(
            rpaDirectory,
            "revisions",
            revision.Value));
        EnsureInsideRoot(path);
        return path;
    }

    private void EnsureInsideRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.Equals(_root, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(
                _root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"O caminho '{fullPath}' escapou do package store.");
        }
    }

    private sealed record CurrentRevisionPointer(string Revision);
}

internal enum FilePackageWriteStage
{
    FlowStaged,
    LocatorsStaged,
    PolicyStaged,
    StagingValidated,
    RevisionPublished,
    CurrentPointerStaged,
    CurrentPointerValidated
}
