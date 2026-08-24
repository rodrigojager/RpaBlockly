using RpaFlow.Contracts.V2;

namespace RpaFlow.Packages;

public sealed record PackageRevision
{
    public PackageRevision(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record RpaPackageOrigin(string Kind, string Location);

public sealed record RpaPackageDocuments(
    FlowDefinition Flow,
    LocatorCatalog Locators,
    RpaPolicyDefinition Policy);

public sealed record PackageWriteResult(
    PackageRevision Revision,
    string ContentHash,
    bool CreatedNewRevision);

public interface IRpaPackageSource
{
    Task<RpaPackageSnapshot> LoadAsync(
        string rpaId,
        PackageRevision? revision,
        CancellationToken cancellationToken);
}

public interface IRpaPackageWriter
{
    Task<PackageWriteResult> PublishAsync(
        string rpaId,
        RpaPackageDocuments documents,
        PackageRevision? expectedRevision,
        CancellationToken cancellationToken);
}

public interface IRpaPackageHistory
{
    Task<IReadOnlyList<PackageRevision>> ListRevisionsAsync(
        string rpaId,
        CancellationToken cancellationToken);
}

public interface IRpaPackageStore :
    IRpaPackageSource,
    IRpaPackageWriter,
    IRpaPackageHistory;

public sealed class PackageRevisionConflictException(string message) :
    InvalidOperationException(message);
