using Microsoft.Playwright;
using RpaFlow.Contracts.V2;

namespace RpaFlow.Playwright.V2.Adaptive;

public interface IAdaptiveCandidateCollector
{
    Task<IReadOnlyList<AdaptiveElementSnapshot>> CollectAsync(
        ILocator candidates,
        int maximumNodes,
        CancellationToken cancellationToken);
}

public interface IElementFingerprintFactory
{
    Task<LocatorFingerprint> CaptureAsync(
        ILocator locator,
        string fingerprintId,
        CancellationToken cancellationToken);
}

public sealed partial class PlaywrightDomFingerprintCollector :
    IAdaptiveCandidateCollector,
    IElementFingerprintFactory
{
    private const int MaximumTextLength = 512;
    private const int MaximumAttributes = 24;
    private const int MaximumAncestors = 8;
    private const int MaximumSiblings = 3;

    public async Task<IReadOnlyList<AdaptiveElementSnapshot>> CollectAsync(
        ILocator candidates,
        int maximumNodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (maximumNodes is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumNodes));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var values = await candidates.EvaluateAllAsync<DomElementDto[]>(
            CollectionScript,
            new
            {
                maximumNodes,
                maximumTextLength = MaximumTextLength,
                maximumAttributes = MaximumAttributes,
                maximumAncestors = MaximumAncestors,
                maximumSiblings = MaximumSiblings
            }).WaitAsync(cancellationToken);
        return values.Select(ToSnapshot).ToArray();
    }

    public async Task<LocatorFingerprint> CaptureAsync(
        ILocator locator,
        string fingerprintId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprintId);
        var snapshots = await CollectAsync(locator, 1, cancellationToken);
        if (snapshots.Count != 1)
        {
            throw new InvalidOperationException(
                $"A captura do fingerprint '{fingerprintId}' exige um elemento singular.");
        }

        var snapshot = snapshots[0];
        return new LocatorFingerprint
        {
            Id = fingerprintId,
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

    private static AdaptiveElementSnapshot ToSnapshot(DomElementDto value) =>
        new(
            value.Index,
            Normalize(value.TagName),
            NormalizeOptional(value.Role),
            NormalizeOptional(value.AccessibleName),
            NormalizeOptional(value.Text),
            SanitizeAttributes(value.Attributes),
            SanitizeNodes(value.Ancestors),
            SanitizeNodes(value.PreviousSiblings),
            SanitizeNodes(value.NextSiblings),
            value.Visible,
            value.Enabled);

    private static IReadOnlyList<LocatorFingerprintNode> SanitizeNodes(
        DomNodeDto[]? nodes) =>
        (nodes ?? [])
            .Select(node => new LocatorFingerprintNode
            {
                TagName = Normalize(node.TagName),
                Role = NormalizeOptional(node.Role),
                Text = NormalizeOptional(node.Text),
                Attributes = SanitizeAttributes(node.Attributes)
            })
            .ToArray();

    private static Dictionary<string, string> SanitizeAttributes(
        Dictionary<string, string>? attributes) =>
        (attributes ?? new Dictionary<string, string>())
            .Where(item => !IsSensitiveAttribute(item.Key))
            .Take(MaximumAttributes)
            .ToDictionary(
                item => item.Key.Trim().ToLowerInvariant(),
                item => Truncate(item.Value.Trim(), MaximumTextLength),
                StringComparer.Ordinal);

    private static bool IsSensitiveAttribute(string name)
    {
        var normalized = name.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return new[]
        {
            "value", "password", "passwd", "secret", "token", "authorization",
            "cookie", "session", "apikey"
        }.Any(normalized.Contains);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Truncate(RegexWhitespace().Replace(value, " ").Trim(), MaximumTextLength);

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];

    [System.Text.RegularExpressions.GeneratedRegex("\\s+")]
    private static partial System.Text.RegularExpressions.Regex RegexWhitespace();

    private sealed class DomElementDto
    {
        public int Index { get; set; }
        public string? TagName { get; set; }
        public string? Role { get; set; }
        public string? AccessibleName { get; set; }
        public string? Text { get; set; }
        public Dictionary<string, string>? Attributes { get; set; }
        public DomNodeDto[]? Ancestors { get; set; }
        public DomNodeDto[]? PreviousSiblings { get; set; }
        public DomNodeDto[]? NextSiblings { get; set; }
        public bool Visible { get; set; }
        public bool Enabled { get; set; }
    }

    private sealed class DomNodeDto
    {
        public string? TagName { get; set; }
        public string? Role { get; set; }
        public string? Text { get; set; }
        public Dictionary<string, string>? Attributes { get; set; }
    }

    private const string CollectionScript =
        """
        (elements, limits) => {
          const sensitive = /(value|pass(word|wd)?|secret|token|authorization|cookie|session|api[-_]?key)/i;
          const allowed = /^(id|name|type|role|class|href|src|title|placeholder|data-testid|data-test|data-qa|aria-[a-z-]+)$/i;
          const excludedTags = new Set(['script', 'style', 'meta', 'link', 'head', 'noscript']);
          const cleanText = (value) => {
            if (!value) return null;
            const normalized = String(value).replace(/\s+/g, ' ').trim();
            return normalized ? normalized.slice(0, limits.maximumTextLength) : null;
          };
          const isPrivate = (element) => {
            if (!element || !(element instanceof Element)) return false;
            if (element.matches('input[type=password]')) return true;
            return Boolean(element.closest('[data-private=true],[data-sensitive=true],[aria-label*=senha i],[name*=password i],[name*=token i]'));
          };
          const attributes = (element) => {
            const result = {};
            if (!element || !(element instanceof Element)) return result;
            let count = 0;
            for (const attribute of Array.from(element.attributes)) {
              if (count >= limits.maximumAttributes) break;
              if (!allowed.test(attribute.name) || sensitive.test(attribute.name)) continue;
              result[attribute.name.toLowerCase()] = String(attribute.value)
                .slice(0, limits.maximumTextLength);
              count++;
            }
            return result;
          };
          const node = (element) => ({
            tagName: element.tagName.toLowerCase(),
            role: element.getAttribute('role'),
            text: isPrivate(element) ? null : cleanText(element.textContent),
            attributes: attributes(element)
          });
          const relatives = (element, direction) => {
            const result = [];
            let current = direction === 'previous'
              ? element.previousElementSibling
              : element.nextElementSibling;
            while (current && result.length < limits.maximumSiblings) {
              result.push(node(current));
              current = direction === 'previous'
                ? current.previousElementSibling
                : current.nextElementSibling;
            }
            return result;
          };
          const result = [];
          const bounded = Array.from(elements).slice(0, limits.maximumNodes);
          for (let index = 0; index < bounded.length; index++) {
            const element = bounded[index];
            if (!(element instanceof Element) || excludedTags.has(element.tagName.toLowerCase())) continue;
            const ancestors = [];
            let parent = element.parentElement;
            while (parent && ancestors.length < limits.maximumAncestors) {
              ancestors.push(node(parent));
              parent = parent.parentElement;
            }
            const style = getComputedStyle(element);
            const rect = element.getBoundingClientRect();
            const visible = style.visibility !== 'hidden' && style.display !== 'none' &&
              Number(style.opacity || 1) > 0 && rect.width > 0 && rect.height > 0;
            const privateElement = isPrivate(element);
            result.push({
              index,
              tagName: element.tagName.toLowerCase(),
              role: element.getAttribute('role'),
              accessibleName: privateElement ? null : cleanText(
                element.getAttribute('aria-label') || element.getAttribute('title') ||
                (element.labels && element.labels[0] ? element.labels[0].textContent : null) ||
                element.textContent),
              text: privateElement ? null : cleanText(element.textContent),
              attributes: attributes(element),
              ancestors,
              previousSiblings: relatives(element, 'previous'),
              nextSiblings: relatives(element, 'next'),
              visible,
              enabled: !element.matches(':disabled,[aria-disabled=true]')
            });
          }
          return result;
        }
        """;
}
