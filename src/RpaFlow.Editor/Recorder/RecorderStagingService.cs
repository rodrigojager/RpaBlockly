using System.Security.Cryptography;
using System.Text;
using RpaFlow.Editor.Configuration;

namespace RpaFlow.Editor.Recorder;

internal sealed class RecorderStagingService
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(2);
    private readonly string _root;
    private readonly RecorderBundleInspector _inspector;
    private readonly ILogger<RecorderStagingService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, StagingEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public RecorderStagingService(
        EditorPaths paths,
        RecorderBundleInspector inspector,
        ILogger<RecorderStagingService> logger)
    {
        _root = Path.GetFullPath(Path.Combine(paths.ProjectRoot, ".recorder-staging"));
        _inspector = inspector;
        _logger = logger;
        EnsureInsideRoot(_root);
        Directory.CreateDirectory(_root);
    }

    public async Task<StagingEntry> CreateAsync(
        byte[] archiveBytes,
        CancellationToken cancellationToken)
    {
        var inspected = await _inspector.InspectAsync(archiveBytes, cancellationToken);
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var directory = ResolveDirectory(id);
        var expiresAt = DateTimeOffset.UtcNow.Add(Retention);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CleanupExpiredCoreAsync(cancellationToken);
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(
                Path.Combine(directory, "bundle.rpablockly.zip"),
                archiveBytes,
                cancellationToken);
            var entry = new StagingEntry(
                id,
                token,
                expiresAt,
                directory,
                inspected,
                null,
                null,
                null);
            _entries.Add(id, entry);
            _logger.LogInformation(
                "Recorder inspect staging={StagingId} bundle={BundleId} steps={Steps} issues={Issues}",
                id,
                inspected.Manifest.BundleId,
                inspected.Manifest.StepCount,
                inspected.Issues.Issues.Count);
            return entry;
        }
        catch
        {
            DeleteDirectory(directory);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StagingEntry> GetAsync(
        string id,
        string token,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CleanupExpiredCoreAsync(cancellationToken);
            if (!_entries.TryGetValue(id, out var entry) ||
                !FixedTimeEquals(entry.Token, token) ||
                entry.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                throw new KeyNotFoundException("Staging Recorder ausente, expirado ou não autorizado.");
            }
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkAppliedAsync(
        string id,
        string token,
        string requestHash,
        string revision,
        IReadOnlyDictionary<string, string> remappings,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_entries.TryGetValue(id, out var entry) || !FixedTimeEquals(entry.Token, token))
            {
                throw new KeyNotFoundException("Staging Recorder não autorizado.");
            }
            _entries[id] = entry with
            {
                AppliedRequestHash = requestHash,
                AppliedRevision = revision,
                AppliedRemappings = new Dictionary<string, string>(remappings)
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(
        string id,
        string token,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_entries.TryGetValue(id, out var entry)) return;
            if (!FixedTimeEquals(entry.Token, token))
            {
                throw new KeyNotFoundException("Staging Recorder não autorizado.");
            }
            _entries.Remove(id);
            DeleteDirectory(entry.Directory);
            _logger.LogInformation("Recorder staging excluído staging={StagingId}", id);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task CleanupExpiredCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var entry in _entries.Values
                     .Where(item => item.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                     .ToArray())
        {
            _entries.Remove(entry.Id);
            DeleteDirectory(entry.Directory);
        }
        return Task.CompletedTask;
    }

    private string ResolveDirectory(string id)
    {
        if (id.Length != 32 || id.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("ID de staging inválido.");
        }
        var directory = Path.GetFullPath(Path.Combine(_root, id));
        EnsureInsideRoot(directory);
        return directory;
    }

    private void DeleteDirectory(string directory)
    {
        EnsureInsideRoot(directory);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private void EnsureInsideRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.Equals(_root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Caminho de staging escapou da raiz permitida.");
        }
    }

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        if (expected.Length != supplied.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(supplied));
    }
}

internal sealed record StagingEntry(
    string Id,
    string Token,
    DateTimeOffset ExpiresAtUtc,
    string Directory,
    InspectedRecorderBundle Bundle,
    string? AppliedRequestHash,
    string? AppliedRevision,
    IReadOnlyDictionary<string, string>? AppliedRemappings);
