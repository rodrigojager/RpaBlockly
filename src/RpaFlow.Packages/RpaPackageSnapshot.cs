using RpaFlow.Contracts.V2;

namespace RpaFlow.Packages;

public sealed class RpaPackageSnapshot
{
    private readonly FlowDefinition _flow;
    private readonly LocatorCatalog _locators;
    private readonly RpaPolicyDefinition _policy;

    public RpaPackageSnapshot(
        string rpaId,
        PackageRevision revision,
        RpaPackageDocuments documents,
        RpaPackageOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rpaId);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(origin);

        RpaPackageValidator.Validate(documents);
        RpaId = rpaId;
        Revision = revision;
        Origin = origin;
        ContentHash = CanonicalJson.ComputePackageHash(documents);
        _flow = Clone(documents.Flow, "flow");
        _locators = Clone(documents.Locators, "locators");
        _policy = Clone(documents.Policy, "policy");
    }

    public string RpaId { get; }

    public PackageRevision Revision { get; }

    public string ContentHash { get; }

    public FlowDefinition Flow => Clone(_flow, "flow");

    public LocatorCatalog Locators => Clone(_locators, "locators");

    public RpaPolicyDefinition Policy => Clone(_policy, "policy");

    public RpaPackageOrigin Origin { get; }

    public RpaPackageDocuments CopyDocuments() => new(
        Clone(_flow, "flow"),
        Clone(_locators, "locators"),
        Clone(_policy, "policy"));

    private static T Clone<T>(T value, string description)
        where T : class =>
        V2JsonSerializer.Deserialize<T>(
            V2JsonSerializer.Serialize(value),
            description);
}
