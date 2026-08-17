namespace RpaFlow.Contracts.V2;

public sealed class RpaPolicyDefinition
{
    public int SchemaVersion { get; set; } = 1;

    public LocatorResiliencePolicy LocatorResilience { get; set; } = new();
}

public sealed class LocatorResiliencePolicy
{
    public LocatorResilienceMode Mode { get; set; } = LocatorResilienceMode.Strict;

    public LearningWriteBackMode LearningWriteBack { get; set; } =
        LearningWriteBackMode.Disabled;

    public LocatorPromotionMode Promotion { get; set; } = LocatorPromotionMode.Disabled;

    public FailedPrimaryBehavior FailedPrimary { get; set; } =
        FailedPrimaryBehavior.Keep;

    public double MinimumConfidence { get; set; } = 0.85;

    public double MinimumRunnerUpGap { get; set; } = 0.10;

    public int MaximumCandidatesPerLocator { get; set; } = 20;

    public int MaximumHeuristicNodes { get; set; } = 5_000;

    public int MaximumResolutionMilliseconds { get; set; } = 30_000;
}

public enum LocatorResilienceMode
{
    Strict,
    Fallback,
    Adaptive
}

public enum LearningWriteBackMode
{
    Disabled,
    Memory,
    Source,
    Overlay
}

public enum LocatorPromotionMode
{
    Disabled,
    AfterSuccessfulExecution
}

public enum FailedPrimaryBehavior
{
    Keep,
    MoveToLast
}
