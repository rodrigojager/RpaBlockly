using System.Collections.Concurrent;
using RpaFlow.Contracts.V2;
using RpaFlow.Packages;

namespace RpaFlow.Playwright.V2.Adaptive;

public enum LocatorLearningCompletionStatus
{
    NoChanges,
    Discarded,
    ConfirmedInMemory,
    Persisted,
    RevisionConflict,
    PersistenceFailed
}

public enum LocatorLearningOutcome
{
    Succeeded,
    Validated,
    Failed,
    Retry,
    Cancelled,
    Unexpected
}

public sealed record LocatorLearningObservation(
    string LocatorId,
    LocatorCandidate Candidate,
    LocatorFingerprint Fingerprint,
    bool FailedPrimary);

public sealed record LocatorLearningCompletion(
    string ExecutionId,
    LocatorLearningCompletionStatus Status,
    PackageRevision? Revision = null,
    string? Detail = null,
    IReadOnlyList<LocatorLearningObservation>? Observations = null);

public sealed class LocatorLearningSession(string executionId)
{
    private readonly ConcurrentDictionary<string, LocatorLearningObservation> _observations =
        new(StringComparer.OrdinalIgnoreCase);

    public string ExecutionId { get; } = executionId;

    public IReadOnlyCollection<LocatorLearningObservation> Observations =>
        _observations.Values.ToArray();

    public void Observe(LocatorLearningObservation observation) =>
        _observations.AddOrUpdate(
            observation.LocatorId,
            observation,
            (_, existing) => Choose(existing, observation));

    public bool TryGet(string locatorId, out LocatorLearningObservation observation) =>
        _observations.TryGetValue(locatorId, out observation!);

    private static LocatorLearningObservation Choose(
        LocatorLearningObservation existing,
        LocatorLearningObservation incoming)
    {
        if (existing.Candidate.Origin != LocatorCandidateOrigin.Heuristic &&
            incoming.Candidate.Origin == LocatorCandidateOrigin.Heuristic)
        {
            return incoming;
        }

        return incoming with { FailedPrimary = existing.FailedPrimary || incoming.FailedPrimary };
    }
}

public sealed class LocatorLearningManager
{
    private readonly ConcurrentDictionary<string, LocatorLearningSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LocatorLearningObservation> _confirmed =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly RpaPackageSnapshot _snapshot;
    private readonly LocatorResiliencePolicy _policy;
    private readonly IRpaPackageWriter? _writer;

    public LocatorLearningManager(
        RpaPackageSnapshot snapshot,
        IRpaPackageWriter? writer = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _policy = snapshot.Policy.LocatorResilience;
        _writer = writer;
        if (_policy.LearningWriteBack is LearningWriteBackMode.Source or
                LearningWriteBackMode.Overlay && writer is null)
        {
            throw new InvalidOperationException(
                $"learningWriteBack {_policy.LearningWriteBack} exige um writer explícito.");
        }
    }

    public LocatorLearningSession Begin(string executionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        var session = new LocatorLearningSession(executionId);
        if (!_sessions.TryAdd(executionId, session))
        {
            throw new InvalidOperationException(
                $"Já existe uma sessão de aprendizado para '{executionId}'.");
        }

        return session;
    }

    public void Observe(string executionId, LocatorLearningObservation observation)
    {
        if (!_sessions.TryGetValue(executionId, out var session))
        {
            throw new InvalidOperationException(
                $"A sessão de aprendizado '{executionId}' não foi iniciada.");
        }

        session.Observe(Clone(observation));
    }

    public bool TryGetOverride(
        string executionId,
        string locatorId,
        out LocatorLearningObservation observation)
    {
        if (_sessions.TryGetValue(executionId, out var session) &&
            session.TryGet(locatorId, out observation))
        {
            observation = Clone(observation);
            return true;
        }

        if (_confirmed.TryGetValue(locatorId, out observation!))
        {
            observation = Clone(observation);
            return true;
        }

        observation = null!;
        return false;
    }

    public async Task<LocatorLearningCompletion> CompleteAsync(
        string executionId,
        LocatorLearningOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryRemove(executionId, out var session))
        {
            return new LocatorLearningCompletion(
                executionId,
                LocatorLearningCompletionStatus.NoChanges);
        }

        if (outcome != LocatorLearningOutcome.Succeeded)
        {
            return new LocatorLearningCompletion(
                executionId,
                LocatorLearningCompletionStatus.Discarded,
                Detail: $"Resultado final: {outcome}.",
                Observations: session.Observations.Select(Clone).ToArray());
        }

        var observations = session.Observations;
        if (observations.Count == 0 ||
            _policy.LearningWriteBack == LearningWriteBackMode.Disabled)
        {
            return new LocatorLearningCompletion(
                executionId,
                LocatorLearningCompletionStatus.NoChanges,
                Observations: observations.Select(Clone).ToArray());
        }

        foreach (var observation in observations)
        {
            _confirmed[observation.LocatorId] = Clone(observation);
        }

        if (_policy.LearningWriteBack == LearningWriteBackMode.Memory)
        {
            return new LocatorLearningCompletion(
                executionId,
                LocatorLearningCompletionStatus.ConfirmedInMemory,
                Observations: observations.Select(Clone).ToArray());
        }

        var updated = Apply(_snapshot.CopyDocuments(), observations, _policy);
        try
        {
            var result = await _writer!.PublishAsync(
                _snapshot.RpaId,
                updated,
                _snapshot.Revision,
                cancellationToken);
            return new LocatorLearningCompletion(
                executionId,
                LocatorLearningCompletionStatus.Persisted,
                result.Revision,
                Observations: observations.Select(Clone).ToArray());
        }
        catch (PackageRevisionConflictException exception)
        {
            RemoveConfirmed(observations);
            return new LocatorLearningCompletion(
                executionId,
                LocatorLearningCompletionStatus.RevisionConflict,
                Detail: exception.Message,
                Observations: observations.Select(Clone).ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RemoveConfirmed(observations);
            return new LocatorLearningCompletion(
                executionId,
                LocatorLearningCompletionStatus.PersistenceFailed,
                Detail: exception.GetType().Name,
                Observations: observations.Select(Clone).ToArray());
        }
    }

    private void RemoveConfirmed(IEnumerable<LocatorLearningObservation> observations)
    {
        foreach (var observation in observations)
        {
            _confirmed.TryRemove(observation.LocatorId, out _);
        }
    }

    internal static RpaPackageDocuments Apply(
        RpaPackageDocuments documents,
        IReadOnlyCollection<LocatorLearningObservation> observations,
        LocatorResiliencePolicy policy)
    {
        var locators = documents.Locators.Locators.ToDictionary(
            locator => locator.Id,
            StringComparer.OrdinalIgnoreCase);
        foreach (var observation in observations.OrderBy(item => item.LocatorId,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (!locators.TryGetValue(observation.LocatorId, out var locator))
            {
                continue;
            }

            var existingFingerprint = locator.Fingerprints.FindIndex(item =>
                item.Id.Equals(observation.Fingerprint.Id, StringComparison.OrdinalIgnoreCase));
            if (existingFingerprint >= 0)
            {
                locator.Fingerprints[existingFingerprint] = observation.Fingerprint;
            }
            else
            {
                if (locator.Fingerprints.Count <
                    LocatorCatalogValidator.MaximumFingerprintsPerLocator)
                {
                    locator.Fingerprints.Add(observation.Fingerprint);
                }
            }

            var candidateIndex = locator.Candidates.FindIndex(item =>
                item.Id.Equals(observation.Candidate.Id, StringComparison.OrdinalIgnoreCase));
            var candidate = candidateIndex >= 0
                ? locator.Candidates[candidateIndex]
                : observation.Candidate;
            if (candidateIndex < 0)
            {
                if (locator.Candidates.Count >= policy.MaximumCandidatesPerLocator)
                {
                    var replaceable = locator.Candidates.FindLastIndex(item =>
                        item.Origin == LocatorCandidateOrigin.Heuristic);
                    if (replaceable < 0)
                    {
                        continue;
                    }

                    locator.Candidates.RemoveAt(replaceable);
                }

                locator.Candidates.Add(candidate);
                candidateIndex = locator.Candidates.Count - 1;
            }

            if (policy.Promotion == LocatorPromotionMode.AfterSuccessfulExecution &&
                candidateIndex > 0)
            {
                locator.Candidates.RemoveAt(candidateIndex);
                candidate.PromotedAtUtc = DateTimeOffset.UtcNow;
                locator.Candidates.Insert(0, candidate);
            }

            if (observation.FailedPrimary &&
                policy.FailedPrimary == FailedPrimaryBehavior.MoveToLast &&
                locator.Candidates.Count > 1)
            {
                var original = locator.Candidates.FirstOrDefault(item =>
                    item.DeveloperRole == DeveloperLocatorRole.Original);
                if (original is not null && !ReferenceEquals(original, candidate))
                {
                    locator.Candidates.Remove(original);
                    locator.Candidates.Add(original);
                }
            }
        }

        RpaPackageValidator.Validate(documents);
        return documents;
    }

    private static LocatorLearningObservation Clone(LocatorLearningObservation value) =>
        V2JsonSerializer.Deserialize<LocatorLearningObservation>(
            V2JsonSerializer.Serialize(value),
            "locator learning observation");
}
