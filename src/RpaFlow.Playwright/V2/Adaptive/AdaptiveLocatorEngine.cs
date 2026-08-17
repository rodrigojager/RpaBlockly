using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;
using RpaFlow.Contracts.V2;
using RpaFlow.Runtime;

namespace RpaFlow.Playwright.V2.Adaptive;

public sealed record AdaptiveLocatorResult(
    ILocator Locator,
    LocatorCandidate LearnedCandidate,
    LocatorFingerprint LearnedFingerprint,
    string SourceFingerprintId,
    double Score,
    double RunnerUpScore,
    int ExaminedNodes,
    long ElapsedMilliseconds);

public sealed class AdaptiveLocatorRejectedException(
    string message,
    double? bestScore = null,
    double? runnerUpScore = null) : InvalidOperationException(message)
{
    public double? BestScore { get; } = bestScore;
    public double? RunnerUpScore { get; } = runnerUpScore;
}

public sealed class AdaptiveLocatorEngine(
    LocatorRecipeCompiler? compiler = null,
    IAdaptiveCandidateCollector? collector = null,
    RpaSafetyAdjustedScorer? scorer = null)
{
    private readonly LocatorRecipeCompiler _compiler =
        compiler ?? new LocatorRecipeCompiler();
    private readonly IAdaptiveCandidateCollector _collector =
        collector ?? new PlaywrightDomFingerprintCollector();
    private readonly RpaSafetyAdjustedScorer _scorer =
        scorer ?? new RpaSafetyAdjustedScorer();

    public async Task<AdaptiveLocatorResult> ResolveAsync(
        IPage page,
        LocatorDefinition definition,
        LocatorUseDefinition use,
        FlowDataContext data,
        LocatorResolutionRequirement requirement,
        LocatorResiliencePolicy policy,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (use.Cardinality == LocatorCardinality.Many)
        {
            throw new AdaptiveLocatorRejectedException(
                "A heurística singular não é aplicada a coleções.");
        }

        if (requirement.State is LocatorRequiredState.Detached or LocatorRequiredState.Hidden)
        {
            throw new AdaptiveLocatorRejectedException(
                "Estados negativos não permitem aprendizado heurístico.");
        }

        if (definition.Fingerprints.Count == 0)
        {
            throw new AdaptiveLocatorRejectedException(
                $"O locator '{definition.Id}' não possui fingerprint para relocalização.");
        }

        var stopwatch = Stopwatch.StartNew();
        var contextRecipe = SelectContextRecipe(definition);
        var candidateSet = _compiler.CompileAdaptiveCandidateSet(page, contextRecipe, data);
        var snapshots = await _collector.CollectAsync(
                candidateSet,
                policy.MaximumHeuristicNodes,
                cancellationToken)
            .WaitAsync(timeout, cancellationToken);
        if (snapshots.Count == 0)
        {
            throw new AdaptiveLocatorRejectedException(
                $"Nenhum nó sanitizado foi coletado para '{definition.Id}'.");
        }

        var rankings = (from fingerprint in definition.Fingerprints
                        from snapshot in snapshots
                        let score = _scorer.Score(fingerprint, snapshot)
                        select new Ranked(fingerprint, snapshot, score))
            .OrderByDescending(item => item.Score.Adjusted)
            .ThenBy(item => item.Snapshot.Index)
            .ThenBy(item => item.Fingerprint.Id, StringComparer.Ordinal)
            .ToArray();
        var best = rankings[0];
        var runnerUp = rankings
            .Skip(1)
            .FirstOrDefault(item => item.Snapshot.Index != best.Snapshot.Index);
        var runnerUpScore = runnerUp?.Score.Adjusted ?? 0d;
        if (best.Score.Adjusted < policy.MinimumConfidence)
        {
            throw new AdaptiveLocatorRejectedException(
                $"Melhor candidato de '{definition.Id}' ficou abaixo da confiança mínima " +
                $"({best.Score.Adjusted:F4} < {policy.MinimumConfidence:F4}).",
                best.Score.Adjusted,
                runnerUpScore);
        }

        if (best.Score.Adjusted - runnerUpScore < policy.MinimumRunnerUpGap)
        {
            throw new AdaptiveLocatorRejectedException(
                $"Melhor candidato de '{definition.Id}' não possui distância segura " +
                $"para o segundo colocado ({best.Score.Adjusted:F4} - " +
                $"{runnerUpScore:F4} < {policy.MinimumRunnerUpGap:F4}).",
                best.Score.Adjusted,
                runnerUpScore);
        }

        if (requirement.State == LocatorRequiredState.Visible && !best.Snapshot.Visible)
        {
            throw new AdaptiveLocatorRejectedException(
                $"Melhor candidato de '{definition.Id}' não está visível.",
                best.Score.Adjusted,
                runnerUpScore);
        }

        var locator = candidateSet.Nth(best.Snapshot.Index);
        var learnedFingerprint = ToFingerprint(
            definition.Id,
            best.Snapshot);
        var learnedCandidate = MaterializeCandidate(
            definition.Id,
            contextRecipe,
            best.Snapshot,
            learnedFingerprint.Id);
        return new AdaptiveLocatorResult(
            locator,
            learnedCandidate,
            learnedFingerprint,
            best.Fingerprint.Id,
            best.Score.Adjusted,
            runnerUpScore,
            snapshots.Count,
            stopwatch.ElapsedMilliseconds);
    }

    private static LocatorRecipe SelectContextRecipe(LocatorDefinition definition)
    {
        var fingerprintRecipe = definition.Candidates.FirstOrDefault(candidate =>
            candidate.Recipe.Target.Strategy == LocatorStrategy.Fingerprint)?.Recipe;
        return fingerprintRecipe ?? definition.Candidates[0].Recipe;
    }

    private static LocatorFingerprint ToFingerprint(
        string locatorId,
        AdaptiveElementSnapshot snapshot)
    {
        var signature = string.Join(
            "|",
            snapshot.TagName,
            snapshot.Role,
            snapshot.AccessibleName,
            string.Join(",", snapshot.Attributes.OrderBy(item => item.Key)
                .Select(item => $"{item.Key}={item.Value}")));
        return new LocatorFingerprint
        {
            Id = $"{locatorId}.learned.{ShortHash(signature)}",
            TagName = snapshot.TagName,
            Role = snapshot.Role,
            AccessibleName = snapshot.AccessibleName,
            Text = snapshot.Text,
            Attributes = new Dictionary<string, string>(
                snapshot.Attributes,
                StringComparer.Ordinal),
            Ancestors = snapshot.Ancestors.ToList(),
            PreviousSiblings = snapshot.PreviousSiblings.ToList(),
            NextSiblings = snapshot.NextSiblings.ToList()
        };
    }

    private static LocatorCandidate MaterializeCandidate(
        string locatorId,
        LocatorRecipe contextRecipe,
        AdaptiveElementSnapshot snapshot,
        string fingerprintId)
    {
        var target = CreateStableExpression(snapshot, fingerprintId);
        var signature = $"{target.Strategy}|{target.Selector}|{target.Role}|" +
            $"{target.Name}|{target.Text}|{target.FingerprintId}";
        return new LocatorCandidate
        {
            Id = $"{locatorId}.heuristic.{ShortHash(signature)}",
            Origin = LocatorCandidateOrigin.Heuristic,
            LearnedAtUtc = DateTimeOffset.UtcNow,
            Recipe = new LocatorRecipe
            {
                Frames = contextRecipe.Frames.Select(CloneExpression).ToList(),
                Scope = contextRecipe.Scope is null
                    ? null
                    : CloneExpression(contextRecipe.Scope),
                Target = target
            }
        };
    }

    private static LocatorExpression CreateStableExpression(
        AdaptiveElementSnapshot snapshot,
        string fingerprintId)
    {
        foreach (var testId in new[] { "data-testid", "data-test", "data-qa" })
        {
            if (snapshot.Attributes.TryGetValue(testId, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return new LocatorExpression
                {
                    Strategy = LocatorStrategy.TestId,
                    Text = value,
                    Exact = true
                };
            }
        }

        if (snapshot.Attributes.TryGetValue("id", out var id) &&
            !string.IsNullOrWhiteSpace(id))
        {
            return new LocatorExpression
            {
                Strategy = LocatorStrategy.Css,
                Selector = $"[id=\"{EscapeCssAttribute(id)}\"]"
            };
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Role) &&
            !string.IsNullOrWhiteSpace(snapshot.AccessibleName))
        {
            return new LocatorExpression
            {
                Strategy = LocatorStrategy.Role,
                Role = snapshot.Role,
                Name = snapshot.AccessibleName,
                Exact = true
            };
        }

        foreach (var attribute in new[] { "name", "type", "placeholder" })
        {
            if (snapshot.Attributes.TryGetValue(attribute, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return new LocatorExpression
                {
                    Strategy = LocatorStrategy.Css,
                    Selector =
                        $"{snapshot.TagName}[{attribute}=\"{EscapeCssAttribute(value)}\"]"
                };
            }
        }

        return new LocatorExpression
        {
            Strategy = LocatorStrategy.Fingerprint,
            FingerprintId = fingerprintId
        };
    }

    private static LocatorExpression CloneExpression(LocatorExpression expression) =>
        new()
        {
            Strategy = expression.Strategy,
            Selector = expression.Selector,
            Role = expression.Role,
            Name = expression.Name,
            Text = expression.Text,
            FingerprintId = expression.FingerprintId,
            Exact = expression.Exact,
            HasText = expression.HasText is null
                ? null
                : new LocatorTextConstraint
                {
                    Literal = expression.HasText.Literal,
                    Source = expression.HasText.Source
                }
        };

    private static string EscapeCssAttribute(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\d ", StringComparison.Ordinal)
            .Replace("\n", "\\a ", StringComparison.Ordinal);

    private static string ShortHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12]
            .ToLowerInvariant();

    private sealed record Ranked(
        LocatorFingerprint Fingerprint,
        AdaptiveElementSnapshot Snapshot,
        SimilarityScore Score);
}
