using RpaFlow.Contracts.V2;

namespace RpaFlow.Packages;

public static class RpaPackageLimits
{
    public const int MaximumFlowBytes = 10 * 1024 * 1024;
    public const int MaximumLocatorsBytes = 15 * 1024 * 1024;
    public const int MaximumPolicyBytes = 1024 * 1024;
    public const int MaximumTotalBytes = 25 * 1024 * 1024;

    public static void ValidateDocumentSizes(RpaPackageDocuments documents)
    {
        var flow = CanonicalJson.Serialize(documents.Flow).Length;
        var locators = CanonicalJson.Serialize(documents.Locators).Length;
        var policy = CanonicalJson.Serialize(documents.Policy).Length;
        if (flow > MaximumFlowBytes)
        {
            throw new InvalidOperationException("flow.production.json excede 10 MiB.");
        }
        if (locators > MaximumLocatorsBytes)
        {
            throw new InvalidOperationException("locators.production.json excede 15 MiB.");
        }
        if (policy > MaximumPolicyBytes)
        {
            throw new InvalidOperationException("rpa.policy.json excede 1 MiB.");
        }
        if ((long)flow + locators + policy > MaximumTotalBytes)
        {
            throw new InvalidOperationException("O pacote V2 excede 25 MiB.");
        }
    }
}
