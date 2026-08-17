using System.Text.Json.Serialization;

namespace RpaFlow.Contracts.V2;

public sealed class LocatorCatalog
{
    public int SchemaVersion { get; set; } = 1;

    public List<LocatorDefinition> Locators { get; set; } = [];
}

public sealed class LocatorDefinition
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public List<LocatorCandidate> Candidates { get; set; } = [];

    public List<LocatorFingerprint> Fingerprints { get; set; } = [];
}

public sealed class LocatorCandidate
{
    public string Id { get; set; } = string.Empty;

    public LocatorCandidateOrigin Origin { get; set; } = LocatorCandidateOrigin.Developer;

    public DeveloperLocatorRole? DeveloperRole { get; set; }

    public RecorderLocatorRole? RecorderRole { get; set; }

    public int? OriginalOrder { get; set; }

    public DateTimeOffset? LearnedAtUtc { get; set; }

    public DateTimeOffset? PromotedAtUtc { get; set; }

    public LocatorRecipe Recipe { get; set; } = new();
}

public sealed class LocatorRecipe
{
    public List<LocatorExpression> Frames { get; set; } = [];

    public LocatorExpression? Scope { get; set; }

    public LocatorExpression Target { get; set; } = new();
}

public sealed class LocatorExpression
{
    public LocatorStrategy Strategy { get; set; }

    public string? Selector { get; set; }

    public string? Role { get; set; }

    public string? Name { get; set; }

    public string? Text { get; set; }

    public string? FingerprintId { get; set; }

    public bool? Exact { get; set; }

    public LocatorTextConstraint? HasText { get; set; }
}

public sealed class LocatorTextConstraint
{
    public string? Literal { get; set; }

    public string? Source { get; set; }
}

public sealed class LocatorFingerprint
{
    public string Id { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;

    public string? Role { get; set; }

    public string? AccessibleName { get; set; }

    public string? Text { get; set; }

    public Dictionary<string, string> Attributes { get; set; } =
        new(StringComparer.Ordinal);

    public List<LocatorFingerprintNode> Ancestors { get; set; } = [];

    public List<LocatorFingerprintNode> PreviousSiblings { get; set; } = [];

    public List<LocatorFingerprintNode> NextSiblings { get; set; } = [];
}

public sealed class LocatorFingerprintNode
{
    public string TagName { get; set; } = string.Empty;

    public string? Role { get; set; }

    public string? Text { get; set; }

    public Dictionary<string, string> Attributes { get; set; } =
        new(StringComparer.Ordinal);
}

public enum LocatorStrategy
{
    Css,
    [JsonStringEnumMemberName("xpath")]
    XPath,
    Role,
    Label,
    Placeholder,
    Text,
    TestId,
    RawPlaywright,
    Fingerprint
}

public enum LocatorCandidateOrigin
{
    Developer,
    Recorder,
    Heuristic
}

public enum DeveloperLocatorRole
{
    Original,
    Alternative
}

public enum RecorderLocatorRole
{
    CapturedPrimary,
    CapturedAlternative
}
