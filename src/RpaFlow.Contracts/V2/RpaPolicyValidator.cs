namespace RpaFlow.Contracts.V2;

public static class RpaPolicyValidator
{
    public static void Validate(RpaPolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"policy.schemaVersion deve ser 1, mas foi {policy.SchemaVersion}.");
        }

        var resilience = policy.LocatorResilience
            ?? throw new InvalidOperationException("locatorResilience é obrigatório.");
        if (resilience.MinimumConfidence is < 0 or > 1)
        {
            throw new InvalidOperationException(
                "minimumConfidence deve estar entre 0 e 1.");
        }

        if (resilience.MinimumRunnerUpGap is < 0 or > 1)
        {
            throw new InvalidOperationException(
                "minimumRunnerUpGap deve estar entre 0 e 1.");
        }

        if (resilience.MaximumCandidatesPerLocator is < 1 or >
            LocatorCatalogValidator.MaximumCandidatesPerLocator)
        {
            throw new InvalidOperationException(
                $"maximumCandidatesPerLocator deve estar entre 1 e " +
                $"{LocatorCatalogValidator.MaximumCandidatesPerLocator}.");
        }

        if (resilience.MaximumHeuristicNodes is < 1 or > 1_000_000)
        {
            throw new InvalidOperationException(
                "maximumHeuristicNodes deve estar entre 1 e 1000000.");
        }

        if (resilience.MaximumResolutionMilliseconds is < 100 or > 600_000)
        {
            throw new InvalidOperationException(
                "maximumResolutionMilliseconds deve estar entre 100 e 600000.");
        }

        if (resilience.Mode != LocatorResilienceMode.Adaptive &&
            (resilience.LearningWriteBack != LearningWriteBackMode.Disabled ||
             resilience.Promotion != LocatorPromotionMode.Disabled))
        {
            throw new InvalidOperationException(
                "Aprendizado e promoção só podem ser habilitados no modo adaptive.");
        }

        if (resilience.LearningWriteBack == LearningWriteBackMode.Disabled &&
            resilience.Promotion != LocatorPromotionMode.Disabled)
        {
            throw new InvalidOperationException(
                "promotion exige learningWriteBack diferente de disabled.");
        }
    }
}
