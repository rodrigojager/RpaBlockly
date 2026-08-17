using RpaFlow.Contracts.V2;

namespace RpaFlow.Playwright.V2.Adaptive;

public interface ISimilarityMetric
{
    double Compare(string? first, string? second);
}

public sealed class ScraplingCompatibleSequenceMatcher : ISimilarityMetric
{
    public double Compare(string? first, string? second)
    {
        first ??= string.Empty;
        second ??= string.Empty;
        if (first.Length == 0 && second.Length == 0)
        {
            return 1d;
        }

        var matches = CountMatching(first.ToCharArray(), second.ToCharArray());
        return 2d * matches / (first.Length + second.Length);
    }

    public double CompareSequence<T>(
        IReadOnlyList<T> first,
        IReadOnlyList<T> second)
    {
        if (first.Count == 0 && second.Count == 0)
        {
            return 1d;
        }

        return 2d * CountMatching(first, second) / (first.Count + second.Count);
    }

    private static int CountMatching<T>(IReadOnlyList<T> first, IReadOnlyList<T> second)
    {
        var pending = new Stack<(int AStart, int AEnd, int BStart, int BEnd)>();
        pending.Push((0, first.Count, 0, second.Count));
        var matches = 0;
        while (pending.Count > 0)
        {
            var range = pending.Pop();
            var block = FindLongestMatch(first, second, range);
            if (block.Length == 0)
            {
                continue;
            }

            matches += block.Length;
            if (range.AStart < block.AStart && range.BStart < block.BStart)
            {
                pending.Push((range.AStart, block.AStart, range.BStart, block.BStart));
            }

            var aAfter = block.AStart + block.Length;
            var bAfter = block.BStart + block.Length;
            if (aAfter < range.AEnd && bAfter < range.BEnd)
            {
                pending.Push((aAfter, range.AEnd, bAfter, range.BEnd));
            }
        }

        return matches;
    }

    private static (int AStart, int BStart, int Length) FindLongestMatch<T>(
        IReadOnlyList<T> first,
        IReadOnlyList<T> second,
        (int AStart, int AEnd, int BStart, int BEnd) range)
    {
        var bestA = range.AStart;
        var bestB = range.BStart;
        var bestLength = 0;
        var previous = new Dictionary<int, int>();
        for (var a = range.AStart; a < range.AEnd; a++)
        {
            var current = new Dictionary<int, int>();
            for (var b = range.BStart; b < range.BEnd; b++)
            {
                if (!EqualityComparer<T>.Default.Equals(first[a], second[b]))
                {
                    continue;
                }

                var length = previous.GetValueOrDefault(b - 1) + 1;
                current[b] = length;
                var candidateA = a - length + 1;
                var candidateB = b - length + 1;
                if (length > bestLength ||
                    (length == bestLength &&
                     (candidateA < bestA || candidateA == bestA && candidateB < bestB)))
                {
                    bestA = candidateA;
                    bestB = candidateB;
                    bestLength = length;
                }
            }

            previous = current;
        }

        return (bestA, bestB, bestLength);
    }
}

public sealed record AdaptiveElementSnapshot(
    int Index,
    string TagName,
    string? Role,
    string? AccessibleName,
    string? Text,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<LocatorFingerprintNode> Ancestors,
    IReadOnlyList<LocatorFingerprintNode> PreviousSiblings,
    IReadOnlyList<LocatorFingerprintNode> NextSiblings,
    bool Visible,
    bool Enabled);

public sealed record SimilarityScore(
    double Baseline,
    double Adjusted,
    IReadOnlyDictionary<string, double> Factors);

public sealed class ScraplingBaselineScorer(ISimilarityMetric? metric = null)
{
    private readonly ISimilarityMetric _metric =
        metric ?? new ScraplingCompatibleSequenceMatcher();
    private readonly ScraplingCompatibleSequenceMatcher _sequenceMetric =
        metric as ScraplingCompatibleSequenceMatcher ??
        new ScraplingCompatibleSequenceMatcher();

    public SimilarityScore Score(
        LocatorFingerprint fingerprint,
        AdaptiveElementSnapshot candidate)
    {
        var factors = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["tag"] = string.Equals(
                fingerprint.TagName,
                candidate.TagName,
                StringComparison.OrdinalIgnoreCase) ? 1d : 0d
        };
        AddTextFactor(factors, "text", fingerprint.Text, candidate.Text);
        factors["attributes"] = CompareAttributes(
            fingerprint.Attributes,
            candidate.Attributes);
        foreach (var key in new[] { "class", "id", "href", "src" })
        {
            if (fingerprint.Attributes.TryGetValue(key, out var expected))
            {
                candidate.Attributes.TryGetValue(key, out var actual);
                factors[key] = _metric.Compare(expected, actual);
            }
        }

        AddTextFactor(
            factors,
            "path",
            SerializePath(fingerprint.TagName, fingerprint.Ancestors),
            SerializePath(candidate.TagName, candidate.Ancestors));
        if (fingerprint.Ancestors.Count > 0)
        {
            var originalParent = fingerprint.Ancestors[0];
            var candidateParent = candidate.Ancestors.FirstOrDefault();
            if (candidateParent is not null)
            {
                factors["parentName"] = _metric.Compare(
                    originalParent.TagName,
                    candidateParent.TagName);
                factors["parentAttributes"] = CompareAttributes(
                    originalParent.Attributes,
                    candidateParent.Attributes);
                AddTextFactor(
                    factors,
                    "parentText",
                    originalParent.Text,
                    candidateParent.Text);
            }
        }

        var originalSiblings = SiblingTags(
            fingerprint.PreviousSiblings,
            fingerprint.NextSiblings);
        if (originalSiblings.Count > 0)
        {
            var candidateSiblings = SiblingTags(
                candidate.PreviousSiblings,
                candidate.NextSiblings);
            factors["siblings"] = SequenceMetric.CompareSequence(
                originalSiblings,
                candidateSiblings);
        }
        var baseline = factors.Values.Average();
        return new SimilarityScore(baseline, baseline, factors);
    }

    private void AddTextFactor(
        IDictionary<string, double> factors,
        string name,
        string? expected,
        string? actual)
    {
        if (!string.IsNullOrWhiteSpace(expected))
        {
            factors[name] = _metric.Compare(expected, actual);
        }
    }

    private double CompareAttributes(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second)
    {
        var firstItems = first.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var secondItems = second.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        return SequenceMetric.CompareSequence(
                   firstItems.Select(item => item.Key).ToArray(),
                   secondItems.Select(item => item.Key).ToArray()) * 0.5d +
            SequenceMetric.CompareSequence(
                firstItems.Select(item => item.Value).ToArray(),
                secondItems.Select(item => item.Value).ToArray()) * 0.5d;
    }

    private ScraplingCompatibleSequenceMatcher SequenceMetric =>
        _sequenceMetric;

    private static string SerializePath(
        string tagName,
        IReadOnlyList<LocatorFingerprintNode> ancestors) =>
        string.Join(
            ">",
            ancestors.Reverse().Select(node => node.TagName).Append(tagName));

    private static IReadOnlyList<string> SiblingTags(
        IReadOnlyList<LocatorFingerprintNode> previous,
        IReadOnlyList<LocatorFingerprintNode> next) =>
        previous.Reverse().Select(node => node.TagName)
            .Concat(next.Select(node => node.TagName))
            .ToArray();
}

public sealed class RpaSafetyAdjustedScorer(
    ScraplingBaselineScorer? baselineScorer = null,
    ISimilarityMetric? metric = null)
{
    private readonly ScraplingBaselineScorer _baseline =
        baselineScorer ?? new ScraplingBaselineScorer(metric);
    private readonly ISimilarityMetric _metric =
        metric ?? new ScraplingCompatibleSequenceMatcher();

    public SimilarityScore Score(
        LocatorFingerprint fingerprint,
        AdaptiveElementSnapshot candidate)
    {
        var baseline = _baseline.Score(fingerprint, candidate);
        var factors = new Dictionary<string, double>(baseline.Factors, StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(fingerprint.Role))
        {
            factors["role"] = _metric.Compare(fingerprint.Role, candidate.Role);
        }

        if (!string.IsNullOrWhiteSpace(fingerprint.AccessibleName))
        {
            factors["accessibleName"] = _metric.Compare(
                fingerprint.AccessibleName,
                candidate.AccessibleName);
        }

        var adjusted = factors.Values.Average();
        if (!candidate.Visible)
        {
            adjusted *= 0.50d;
        }

        if (!candidate.Enabled)
        {
            adjusted *= 0.75d;
        }

        return new SimilarityScore(
            baseline.Baseline,
            Math.Clamp(adjusted, 0d, 1d),
            factors);
    }
}
