using System.Diagnostics;
using System.Globalization;
using Microsoft.Playwright;
using RpaFlow.Packages;
using RpaFlow.Contracts.V2;
using RpaFlow.Runtime;
using RpaFlow.Playwright.V2.Adaptive;

namespace RpaFlow.Playwright.V2;

public interface ILocatorResolver
{
    Task<LocatorResolutionResult> ResolveAsync(
        IPage page,
        LocatorUseDefinition use,
        FlowDataContext data,
        LocatorResolutionRequirement requirement,
        TimeSpan? timeout,
        CancellationToken cancellationToken,
        LocatorResolutionEventContext? eventContext = null);
}

public sealed class LocatorResolver : ILocatorResolver
{
    private readonly IReadOnlyDictionary<string, LocatorDefinition> _locators;
    private readonly LocatorResiliencePolicy _policy;
    private readonly LocatorRecipeCompiler _compiler;
    private readonly AdaptiveLocatorEngine _adaptiveEngine;
    private readonly LocatorLearningManager? _learning;
    private readonly IElementFingerprintFactory _fingerprintFactory;
    private readonly IFlowExecutionObserver _observer;
    private readonly string? _rpaId;
    private readonly string? _packageOrigin;
    private readonly string? _packageRevision;
    private readonly string? _packageHash;

    public LocatorResolver(
        LocatorCatalog catalog,
        RpaPolicyDefinition policy,
        LocatorRecipeCompiler? compiler = null,
        AdaptiveLocatorEngine? adaptiveEngine = null,
        LocatorLearningManager? learning = null,
        IElementFingerprintFactory? fingerprintFactory = null,
        IFlowExecutionObserver? observer = null,
        RpaPackageSnapshot? snapshot = null)
    {
        LocatorCatalogValidator.Validate(catalog);
        RpaPolicyValidator.Validate(policy);
        var catalogCopy = V2JsonSerializer.Deserialize<LocatorCatalog>(
            V2JsonSerializer.Serialize(catalog),
            "locators");
        var policyCopy = V2JsonSerializer.Deserialize<RpaPolicyDefinition>(
            V2JsonSerializer.Serialize(policy),
            "policy");
        _locators = catalogCopy.Locators.ToDictionary(
            locator => locator.Id,
            StringComparer.OrdinalIgnoreCase);
        _policy = policyCopy.LocatorResilience;
        _compiler = compiler ?? new LocatorRecipeCompiler();
        _adaptiveEngine = adaptiveEngine ?? new AdaptiveLocatorEngine(_compiler);
        _learning = learning;
        _fingerprintFactory =
            fingerprintFactory ?? new PlaywrightDomFingerprintCollector();
        _observer = observer ?? NullFlowExecutionObserver.Instance;
        _rpaId = snapshot?.RpaId;
        _packageOrigin = snapshot?.Origin.Kind;
        _packageRevision = snapshot?.Revision.Value;
        _packageHash = snapshot?.ContentHash;
    }

    public async Task<LocatorResolutionResult> ResolveAsync(
        IPage page,
        LocatorUseDefinition use,
        FlowDataContext data,
        LocatorResolutionRequirement requirement,
        TimeSpan? timeout,
        CancellationToken cancellationToken,
        LocatorResolutionEventContext? eventContext = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(use);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(requirement);
        if (!_locators.TryGetValue(use.LocatorId, out var locatorDefinition))
        {
            throw new InvalidOperationException(
                $"O locator '{use.LocatorId}' não existe no snapshot.");
        }

        var maximum = TimeSpan.FromMilliseconds(_policy.MaximumResolutionMilliseconds);
        var budget = timeout is null || timeout > maximum ? maximum : timeout.Value;
        if (budget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var executionId = ResolveExecutionId(data);
        await ObserveAsync(
            "locatorResolutionStarted",
            executionId,
            use.LocatorId,
            eventContext,
            cancellationToken: cancellationToken);
        var catalogCandidates = (_policy.Mode == LocatorResilienceMode.Strict
                ? locatorDefinition.Candidates.Take(1)
                : locatorDefinition.Candidates)
            .Where(IsExactCandidate)
            .ToArray();
        var originalPrimaryId = catalogCandidates.FirstOrDefault()?.Id;
        var candidates = catalogCandidates.AsEnumerable();
        if (_learning is not null &&
            executionId is not null &&
            _learning.TryGetOverride(executionId, use.LocatorId, out var learned) &&
            IsExactCandidate(learned.Candidate))
        {
            candidates = new[] { learned.Candidate }.Concat(candidates);
        }

        var orderedCandidates = candidates
            .DistinctBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var attempts = new List<LocatorResolutionAttempt>(
            orderedCandidates.Length +
            (_policy.Mode == LocatorResilienceMode.Adaptive ? 1 : 0));
        var total = Stopwatch.StartNew();
        for (var index = 0; index < orderedCandidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = budget - total.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                attempts.Add(new LocatorResolutionAttempt(
                    orderedCandidates[index].Id,
                    index,
                    Succeeded: false,
                    LocatorResolutionFailureReason.Timeout,
                    MatchCount: null,
                    total.ElapsedMilliseconds,
                    "orçamento total esgotado"));
                break;
            }

            var remainingCandidates = orderedCandidates.Length - index +
                (_policy.Mode == LocatorResilienceMode.Adaptive ? 1 : 0);
            var sliceMilliseconds = Math.Max(
                100,
                remaining.TotalMilliseconds / remainingCandidates);
            var slice = TimeSpan.FromMilliseconds(
                Math.Min(remaining.TotalMilliseconds, sliceMilliseconds));
            var attemptWatch = Stopwatch.StartNew();
            try
            {
                var compiled = _compiler.Compile(
                    page,
                    orderedCandidates[index].Recipe,
                    data);
                var checkedLocator = await CheckAsync(
                    compiled,
                    use.Cardinality,
                    requirement,
                    slice,
                    cancellationToken);
                attempts.Add(new LocatorResolutionAttempt(
                    orderedCandidates[index].Id,
                    index,
                    Succeeded: true,
                    FailureReason: null,
                    checkedLocator.Count,
                    attemptWatch.ElapsedMilliseconds,
                    null));
                await ObserveExactSuccessAsync(
                    executionId,
                    use,
                    orderedCandidates[index],
                    checkedLocator.Locator,
                    attempts,
                    originalPrimaryId,
                    requirement,
                    cancellationToken);
                await ObserveAttemptsAsync(
                    executionId,
                    use.LocatorId,
                    attempts,
                    eventContext,
                    completed: true,
                    cancellationToken);
                return new LocatorResolutionResult(
                    use.LocatorId,
                    orderedCandidates[index],
                    checkedLocator.Locator,
                    attempts,
                    total.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var (reason, count) = Classify(exception);
                attempts.Add(new LocatorResolutionAttempt(
                    orderedCandidates[index].Id,
                    index,
                    Succeeded: false,
                    reason,
                    count,
                    attemptWatch.ElapsedMilliseconds,
                    SanitizeDetail(exception)));
                if (reason == LocatorResolutionFailureReason.PageOrContextClosed)
                {
                    break;
                }
            }
        }


        if (_policy.Mode == LocatorResilienceMode.Adaptive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = budget - total.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                var adaptiveWatch = Stopwatch.StartNew();
                try
                {
                    var adaptive = await _adaptiveEngine.ResolveAsync(
                        page,
                        locatorDefinition,
                        use,
                        data,
                        requirement,
                        _policy,
                        remaining,
                        cancellationToken);
                    ObserveHeuristicSuccess(
                        executionId,
                        use.LocatorId,
                        adaptive,
                        attempts,
                        originalPrimaryId);
                    attempts.Add(new LocatorResolutionAttempt(
                        adaptive.LearnedCandidate.Id,
                        attempts.Count,
                        Succeeded: true,
                        FailureReason: null,
                        MatchCount: 1,
                        adaptiveWatch.ElapsedMilliseconds,
                        "heurística aceita; confiança=" +
                        adaptive.Score.ToString("F4", CultureInfo.InvariantCulture) +
                        "; segundo=" +
                        adaptive.RunnerUpScore.ToString("F4", CultureInfo.InvariantCulture) +
                        $"; nós={adaptive.ExaminedNodes}"));
                    await ObserveAttemptsAsync(
                        executionId,
                        use.LocatorId,
                        attempts,
                        eventContext,
                        completed: true,
                        cancellationToken);
                    return new LocatorResolutionResult(
                        use.LocatorId,
                        adaptive.LearnedCandidate,
                        adaptive.Locator,
                        attempts,
                        total.ElapsedMilliseconds,
                        UsedHeuristic: true,
                        adaptive.LearnedFingerprint,
                        adaptive.Score,
                        adaptive.RunnerUpScore);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (AdaptiveLocatorRejectedException exception)
                {
                    var reason = exception.BestScore.HasValue &&
                        exception.RunnerUpScore.HasValue &&
                        exception.BestScore.Value - exception.RunnerUpScore.Value <
                            _policy.MinimumRunnerUpGap
                            ? LocatorResolutionFailureReason.Ambiguous
                            : LocatorResolutionFailureReason.NotFound;
                    attempts.Add(new LocatorResolutionAttempt(
                        $"heuristic:{use.LocatorId}",
                        attempts.Count,
                        Succeeded: false,
                        reason,
                        MatchCount: null,
                        adaptiveWatch.ElapsedMilliseconds,
                        SanitizeDetail(exception)));
                }
                catch (System.TimeoutException exception)
                {
                    attempts.Add(new LocatorResolutionAttempt(
                        $"heuristic:{use.LocatorId}",
                        attempts.Count,
                        Succeeded: false,
                        LocatorResolutionFailureReason.Timeout,
                        MatchCount: null,
                        adaptiveWatch.ElapsedMilliseconds,
                        SanitizeDetail(exception)));
                }
                catch (PlaywrightException exception) when (IsClosed(exception))
                {
                    attempts.Add(new LocatorResolutionAttempt(
                        $"heuristic:{use.LocatorId}",
                        attempts.Count,
                        Succeeded: false,
                        LocatorResolutionFailureReason.PageOrContextClosed,
                        MatchCount: null,
                        adaptiveWatch.ElapsedMilliseconds,
                        SanitizeDetail(exception)));
                }
            }
        }

        await ObserveAttemptsAsync(
            executionId,
            use.LocatorId,
            attempts,
            eventContext,
            completed: false,
            CancellationToken.None);
        throw new LocatorResolutionException(use.LocatorId, attempts);
    }

    private static bool IsExactCandidate(LocatorCandidate candidate) =>
        candidate.Recipe.Target.Strategy != LocatorStrategy.Fingerprint;

    private async Task ObserveExactSuccessAsync(
        string? executionId,
        LocatorUseDefinition use,
        LocatorCandidate candidate,
        ILocator locator,
        IReadOnlyList<LocatorResolutionAttempt> attempts,
        string? originalPrimaryId,
        LocatorResolutionRequirement requirement,
        CancellationToken cancellationToken)
    {
        if (_learning is null || executionId is null ||
            use.Cardinality == LocatorCardinality.Many ||
            requirement.State is LocatorRequiredState.Detached or LocatorRequiredState.Hidden)
        {
            return;
        }

        try
        {
            var fingerprint = await _fingerprintFactory.CaptureAsync(
                locator,
                $"{use.LocatorId}.observed",
                cancellationToken);
            _learning.Observe(
                executionId,
                new LocatorLearningObservation(
                    use.LocatorId,
                    candidate,
                    fingerprint,
                    FailedPrimary(attempts, originalPrimaryId)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A captura adaptativa não pode invalidar um locator exato já aceito.
        }
    }

    private void ObserveHeuristicSuccess(
        string? executionId,
        string locatorId,
        AdaptiveLocatorResult result,
        IReadOnlyList<LocatorResolutionAttempt> attempts,
        string? originalPrimaryId)
    {
        if (_learning is null || executionId is null)
        {
            return;
        }

        _learning.Observe(
            executionId,
            new LocatorLearningObservation(
                locatorId,
                result.LearnedCandidate,
                result.LearnedFingerprint,
                FailedPrimary(attempts, originalPrimaryId)));
    }

    private static bool FailedPrimary(
        IReadOnlyList<LocatorResolutionAttempt> attempts,
        string? originalPrimaryId) =>
        originalPrimaryId is not null && attempts.Any(attempt =>
            attempt.CandidateId.Equals(originalPrimaryId, StringComparison.OrdinalIgnoreCase) &&
            !attempt.Succeeded);

    private static string? ResolveExecutionId(FlowDataContext data)
    {
        if (!data.TryResolve("system.executionId", out var node))
        {
            return null;
        }

        return FlowValueResolver.ConvertSimpleValue(node, "system.executionId");
    }

    private async Task ObserveAttemptsAsync(
        string? executionId,
        string locatorId,
        IReadOnlyList<LocatorResolutionAttempt> attempts,
        LocatorResolutionEventContext? context,
        bool completed,
        CancellationToken cancellationToken)
    {
        foreach (var attempt in attempts)
        {
            var kind = attempt.Succeeded
                ? attempt.CandidateIndex == 0
                    ? "locatorCandidateAccepted"
                    : "locatorFallbackSelected"
                : attempt.CandidateId.StartsWith("heuristic:", StringComparison.Ordinal)
                    ? "locatorHeuristicRejected"
                    : "locatorCandidateRejected";
            await ObserveAsync(
                kind,
                executionId,
                locatorId,
                context,
                attempt.CandidateId,
                attempt.FailureReason?.ToString(),
                attempt.ElapsedMilliseconds,
                attempt.Detail,
                cancellationToken);
        }

        await ObserveAsync(
            completed ? "locatorResolutionCompleted" : "locatorResolutionFailed",
            executionId,
            locatorId,
            context,
            elapsedMilliseconds: attempts.Sum(item => item.ElapsedMilliseconds),
            cancellationToken: cancellationToken);
    }

    private async Task ObserveAsync(
        string kind,
        string? executionId,
        string locatorId,
        LocatorResolutionEventContext? context,
        string? candidateId = null,
        string? reason = null,
        long? elapsedMilliseconds = null,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executionId))
        {
            return;
        }

        try
        {
            await _observer.ObserveAsync(
                new FlowExecutionEvent(
                    kind,
                    executionId,
                    WorkItemId: null,
                    BatchId: null,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    context?.Action.Id,
                    context?.Action.Name,
                    context?.Action.Type,
                    ElapsedMilliseconds: elapsedMilliseconds,
                    RpaId: _rpaId,
                    PackageOrigin: _packageOrigin,
                    PackageRevision: _packageRevision,
                    PackageHash: _packageHash,
                    LocatorId: locatorId,
                    CandidateId: candidateId,
                    ResolutionReason: reason,
                    Detail: detail),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Diagnóstico não pode interromper a resolução do locator.
        }
    }

    private static async Task<CheckedLocator> CheckAsync(
        ILocator locator,
        LocatorCardinality cardinality,
        LocatorResolutionRequirement requirement,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (requirement.State != LocatorRequiredState.Any)
        {
            var waitState = requirement.State switch
            {
                LocatorRequiredState.Attached => WaitForSelectorState.Attached,
                LocatorRequiredState.Visible => WaitForSelectorState.Visible,
                LocatorRequiredState.Detached => WaitForSelectorState.Detached,
                LocatorRequiredState.Hidden => WaitForSelectorState.Hidden,
                _ => throw new ArgumentOutOfRangeException(nameof(requirement))
            };
            try
            {
                await locator.First.WaitForAsync(new LocatorWaitForOptions
                {
                    State = waitState,
                    Timeout = (float)timeout.TotalMilliseconds
                }).WaitAsync(timeout, cancellationToken);
            }
            catch (System.TimeoutException exception)
            {
                throw new LocatorCheckException(
                    LocatorResolutionFailureReason.Timeout,
                    count: null,
                    exception.Message,
                    exception);
            }
            catch (PlaywrightException exception)
            {
                throw new LocatorCheckException(
                    IsTimeout(exception)
                        ? LocatorResolutionFailureReason.Timeout
                        : IsClosed(exception)
                            ? LocatorResolutionFailureReason.PageOrContextClosed
                            : LocatorResolutionFailureReason.InvalidState,
                    count: null,
                    exception.Message,
                    exception);
            }
        }

        var count = await locator.CountAsync().WaitAsync(timeout, cancellationToken);
        if (count == 0)
        {
            if (requirement.AllowEmpty ||
                requirement.State is LocatorRequiredState.Detached or LocatorRequiredState.Hidden)
            {
                return new CheckedLocator(locator, count);
            }

            throw new LocatorCheckException(
                LocatorResolutionFailureReason.NotFound,
                count,
                "nenhum elemento encontrado");
        }

        return cardinality switch
        {
            LocatorCardinality.Single when count == 1 => new CheckedLocator(locator, count),
            LocatorCardinality.Single => throw new LocatorCheckException(
                LocatorResolutionFailureReason.Ambiguous,
                count,
                $"esperado um elemento, encontrados {count}"),
            LocatorCardinality.First => new CheckedLocator(locator.First, count),
            LocatorCardinality.Many => new CheckedLocator(locator, count),
            _ => throw new ArgumentOutOfRangeException(
                nameof(cardinality),
                cardinality,
                "Cardinalidade desconhecida.")
        };
    }

    private static (LocatorResolutionFailureReason Reason, int? Count) Classify(
        Exception exception) =>
        exception switch
        {
            LocatorCheckException check => (check.Reason, check.Count),
            NotSupportedException =>
                (LocatorResolutionFailureReason.UnsupportedStrategy, null),
            ArgumentException or InvalidOperationException =>
                (LocatorResolutionFailureReason.InvalidRecipe, null),
            System.TimeoutException =>
                (LocatorResolutionFailureReason.Timeout, null),
            PlaywrightException playwright when IsTimeout(playwright) =>
                (LocatorResolutionFailureReason.Timeout, null),
            PlaywrightException playwright when IsClosed(playwright) =>
                (LocatorResolutionFailureReason.PageOrContextClosed, null),
            _ => (LocatorResolutionFailureReason.InvalidState, null)
        };

    private static bool IsTimeout(PlaywrightException exception) =>
        exception.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("exceeded", StringComparison.OrdinalIgnoreCase);

    private static bool IsClosed(PlaywrightException exception) =>
        exception.Message.Contains("has been closed", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("Target page, context or browser has been closed",
            StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("TargetClosedError", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeDetail(Exception exception) => exception switch
    {
        LocatorCheckException check => check.Message,
        AdaptiveLocatorRejectedException adaptive =>
            "Heurística rejeitada com segurança" +
            (adaptive.BestScore.HasValue
                ? "; melhor=" + adaptive.BestScore.Value.ToString(
                    "F4",
                    CultureInfo.InvariantCulture)
                : string.Empty) +
            (adaptive.RunnerUpScore.HasValue
                ? "; segundo=" + adaptive.RunnerUpScore.Value.ToString(
                    "F4",
                    CultureInfo.InvariantCulture)
                : string.Empty),
        System.TimeoutException => "Tempo da tentativa esgotado.",
        PlaywrightException => "Falha Playwright durante a resolução.",
        NotSupportedException => "Estratégia não suportada.",
        ArgumentException or InvalidOperationException => "Receita de locator inválida.",
        _ => "Falha de estado durante a resolução."
    };

    private sealed record CheckedLocator(ILocator Locator, int Count);

    private sealed class LocatorCheckException : InvalidOperationException
    {
        public LocatorCheckException(
            LocatorResolutionFailureReason reason,
            int? count,
            string message,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Reason = reason;
            Count = count;
        }

        public LocatorResolutionFailureReason Reason { get; }

        public int? Count { get; }
    }
}
