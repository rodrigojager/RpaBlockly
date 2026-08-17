using RpaFlow.Contracts.V2;

namespace RpaFlow.Packages;

public sealed record RpaPackageValidationResult(IReadOnlyList<string> Warnings);

public static class RpaPackageValidator
{
    public static RpaPackageValidationResult Validate(RpaPackageDocuments documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        FlowDefinitionValidator.Validate(documents.Flow);
        LocatorCatalogValidator.Validate(documents.Locators);
        RpaPolicyValidator.Validate(documents.Policy);
        RpaPackageLimits.ValidateDocumentSizes(documents);

        var locators = documents.Locators.Locators.ToDictionary(
            locator => locator.Id,
            StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, role, use) in
                 FlowDefinitionValidator.EnumerateLocatorUses(documents.Flow))
        {
            if (!locators.TryGetValue(use.LocatorId, out var locator))
            {
                throw new InvalidOperationException(
                    $"{path} referencia o locator inexistente '{use.LocatorId}'.");
            }

            used.Add(locator.Id);
            ValidateCardinality(path, role, use);
        }

        foreach (var locator in documents.Locators.Locators)
        {
            if (locator.Candidates.Count > documents.Policy.LocatorResilience
                    .MaximumCandidatesPerLocator)
            {
                throw new InvalidOperationException(
                    $"O locator '{locator.Id}' excede o limite da política.");
            }

            ValidateFingerprintReferences(locator);
        }

        var warnings = documents.Locators.Locators
            .Where(locator => !used.Contains(locator.Id))
            .Select(locator => $"Locator não utilizado: {locator.Id}.")
            .ToArray();
        return new RpaPackageValidationResult(warnings);
    }

    private static void ValidateCardinality(
        string path,
        string role,
        LocatorUseDefinition use)
    {
        if (role is "trigger" or "ready" or "success" or "protocol" or "condition" &&
            use.Cardinality == LocatorCardinality.Many)
        {
            throw new InvalidOperationException(
                $"{path} não aceita cardinalidade many.");
        }

        if (role == "options" && use.Cardinality != LocatorCardinality.Many)
        {
            throw new InvalidOperationException(
                $"{path} deve usar cardinalidade many.");
        }

    }

    private static void ValidateFingerprintReferences(LocatorDefinition locator)
    {
        var fingerprintIds = locator.Fingerprints
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in locator.Candidates)
        {
            foreach (var expression in candidate.Recipe.Frames
                         .Append(candidate.Recipe.Scope)
                         .Append(candidate.Recipe.Target)
                         .Where(item => item is not null))
            {
                if (expression!.Strategy == LocatorStrategy.Fingerprint &&
                    !fingerprintIds.Contains(expression.FingerprintId!))
                {
                    throw new InvalidOperationException(
                        $"O candidato '{candidate.Id}' referencia o fingerprint " +
                        $"inexistente '{expression.FingerprintId}'.");
                }
            }
        }
    }
}
