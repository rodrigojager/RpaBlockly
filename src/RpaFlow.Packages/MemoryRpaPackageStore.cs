using System.Collections.Concurrent;

namespace RpaFlow.Packages;

public sealed class MemoryRpaPackageStore : IRpaPackageStore
{
    private readonly ConcurrentDictionary<string, PackageState> _packages =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<RpaPackageSnapshot> LoadAsync(
        string rpaId,
        PackageRevision? revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_packages.TryGetValue(rpaId, out var state))
        {
            throw new KeyNotFoundException($"O pacote '{rpaId}' não existe.");
        }

        lock (state.SyncRoot)
        {
            var selected = revision ?? state.Current
                ?? throw new KeyNotFoundException($"O pacote '{rpaId}' não possui revisão atual.");
            if (!state.Revisions.TryGetValue(selected.Value, out var snapshot))
            {
                throw new KeyNotFoundException(
                    $"A revisão '{selected}' do pacote '{rpaId}' não existe.");
            }

            return Task.FromResult(Clone(snapshot));
        }
    }

    public Task<PackageWriteResult> PublishAsync(
        string rpaId,
        RpaPackageDocuments documents,
        PackageRevision? expectedRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RpaPackageStoreRules.ValidateRpaId(rpaId);
        RpaPackageValidator.Validate(documents);
        var hash = CanonicalJson.ComputePackageHash(documents);
        var revision = new PackageRevision(hash);
        var state = _packages.GetOrAdd(rpaId, _ => new PackageState());
        lock (state.SyncRoot)
        {
            RpaPackageStoreRules.EnsureExpectedRevision(
                rpaId,
                state.Current,
                expectedRevision);
            var created = !state.Revisions.ContainsKey(revision.Value);
            if (created)
            {
                state.Revisions.Add(
                    revision.Value,
                    new RpaPackageSnapshot(
                        rpaId,
                        revision,
                        documents,
                        new RpaPackageOrigin("memory", rpaId)));
            }

            state.Current = revision;
            return Task.FromResult(new PackageWriteResult(revision, hash, created));
        }
    }

    public Task<IReadOnlyList<PackageRevision>> ListRevisionsAsync(
        string rpaId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_packages.TryGetValue(rpaId, out var state))
        {
            return Task.FromResult<IReadOnlyList<PackageRevision>>([]);
        }

        lock (state.SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<PackageRevision>>(
                state.Revisions.Keys
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .Select(value => new PackageRevision(value))
                    .ToArray());
        }
    }

    private static RpaPackageSnapshot Clone(RpaPackageSnapshot snapshot) => new(
        snapshot.RpaId,
        snapshot.Revision,
        snapshot.CopyDocuments(),
        snapshot.Origin);

    private sealed class PackageState
    {
        public object SyncRoot { get; } = new();

        public Dictionary<string, RpaPackageSnapshot> Revisions { get; } =
            new(StringComparer.Ordinal);

        public PackageRevision? Current { get; set; }
    }
}
