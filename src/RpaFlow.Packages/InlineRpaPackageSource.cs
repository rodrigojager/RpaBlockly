namespace RpaFlow.Packages;

public sealed class InlineRpaPackageSource : IRpaPackageSource
{
    private readonly RpaPackageSnapshot _snapshot;

    public InlineRpaPackageSource(string rpaId, RpaPackageDocuments documents)
    {
        RpaPackageValidator.Validate(documents);
        var hash = CanonicalJson.ComputePackageHash(documents);
        _snapshot = new RpaPackageSnapshot(
            rpaId,
            new PackageRevision(hash),
            documents,
            new RpaPackageOrigin("inline", rpaId));
    }

    public Task<RpaPackageSnapshot> LoadAsync(
        string rpaId,
        PackageRevision? revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!rpaId.Equals(_snapshot.RpaId, StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyNotFoundException($"O pacote inline '{rpaId}' não existe.");
        }

        if (revision is not null &&
            !revision.Value.Equals(_snapshot.Revision.Value, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException(
                $"A revisão inline '{revision}' não existe para '{rpaId}'.");
        }

        return Task.FromResult(new RpaPackageSnapshot(
            _snapshot.RpaId,
            _snapshot.Revision,
            _snapshot.CopyDocuments(),
            _snapshot.Origin));
    }
}
