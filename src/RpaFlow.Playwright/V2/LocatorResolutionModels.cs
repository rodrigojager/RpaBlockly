using Microsoft.Playwright;
using RpaFlow.Contracts.V2;

namespace RpaFlow.Playwright.V2;

public enum LocatorRequiredState
{
    Any,
    Attached,
    Visible,
    Detached,
    Hidden
}

public enum LocatorResolutionFailureReason
{
    NotFound,
    Ambiguous,
    InvalidState,
    Timeout,
    PageOrContextClosed,
    InvalidRecipe,
    UnsupportedStrategy
}

public sealed record LocatorResolutionRequirement(
    LocatorRequiredState State = LocatorRequiredState.Attached,
    bool AllowEmpty = false);

public sealed record LocatorResolutionEventContext(FlowActionIdentity Action);

public sealed record LocatorResolutionAttempt(
    string CandidateId,
    int CandidateIndex,
    bool Succeeded,
    LocatorResolutionFailureReason? FailureReason,
    int? MatchCount,
    long ElapsedMilliseconds,
    string? Detail);

public sealed record LocatorResolutionResult(
    string LocatorId,
    LocatorCandidate Candidate,
    ILocator Locator,
    IReadOnlyList<LocatorResolutionAttempt> Attempts,
    long ElapsedMilliseconds,
    bool UsedHeuristic = false,
    LocatorFingerprint? LearnedFingerprint = null,
    double? Confidence = null,
    double? RunnerUpConfidence = null);

public sealed class LocatorResolutionException : InvalidOperationException
{
    public LocatorResolutionException(
        string locatorId,
        IReadOnlyList<LocatorResolutionAttempt> attempts)
        : base(CreateMessage(locatorId, attempts))
    {
        LocatorId = locatorId;
        Attempts = attempts;
    }

    public string LocatorId { get; }

    public IReadOnlyList<LocatorResolutionAttempt> Attempts { get; }

    private static string CreateMessage(
        string locatorId,
        IReadOnlyList<LocatorResolutionAttempt> attempts)
    {
        var reasons = attempts.Count == 0
            ? "nenhuma tentativa foi executada"
            : string.Join(
                "; ",
                attempts.Select(attempt =>
                    $"{attempt.CandidateId}: {attempt.FailureReason} " +
                    $"({attempt.Detail ?? "sem detalhe"})"));
        return $"Não foi possível resolver o locator '{locatorId}': {reasons}.";
    }
}
