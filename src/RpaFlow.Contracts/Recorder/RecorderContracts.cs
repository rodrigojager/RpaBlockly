using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RpaFlow.Contracts.Recorder;

public static class RecorderBundleLimits
{
    public const int MaximumEntries = 500;
    public const long MaximumCompressedEntryBytes = 10 * 1024 * 1024;
    public const long MaximumUncompressedEntryBytes = 25 * 1024 * 1024;
    public const long MaximumTotalUncompressedBytes = 100 * 1024 * 1024;
    public const double MaximumCompressionRatio = 100;
    public const int MaximumEvidenceItems = 200;
    public const long MaximumEvidenceBytes = 5 * 1024 * 1024;
    public const long MaximumUploadBytes = 20 * 1024 * 1024;
    public const long MaximumTotalUploadBytes = 50 * 1024 * 1024;
    public const int MaximumSessionEvents = 100_000;
    public const int MaximumSessionDurationMinutes = 480;
    public const int MaximumTextLength = 2_000;
}

public sealed class RecorderBundleManifest
{
    public string BundleFormat { get; set; } = "rpablockly-recorder";
    public int BundleVersion { get; set; } = 1;
    public string BundleId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string RecorderVersion { get; set; } = "1.0.0";
    public string GeneratorVersion { get; set; } = "1.0.0";
    public string RpaPackageRoot { get; set; } = "package";
    public RecorderSchemaVersions Schemas { get; set; } = new();
    public string DisplayName { get; set; } = string.Empty;
    public string Origin { get; set; } = "chrome-recorder";
    public string? RecipientKeyId { get; set; }
    public bool HasSecrets { get; set; }
    public bool HasUploads { get; set; }
    public int StepCount { get; set; }
    public int BlockingIssueCount { get; set; }
    public int WarningIssueCount { get; set; }
    public List<string> Files { get; set; } = [];
    public bool ContainsReplay { get; set; }
}

public sealed class RecorderSchemaVersions
{
    public int Flow { get; set; } = 2;
    public int Locators { get; set; } = 1;
    public int Policy { get; set; } = 1;
    public int Session { get; set; } = 1;
    public int Evidence { get; set; } = 1;
    public int Issues { get; set; } = 1;
    public int Integrity { get; set; } = 1;
}

public sealed class RecorderSessionDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string SessionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RecorderSessionState State { get; set; } = RecorderSessionState.Idle;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string Locale { get; set; } = "pt-BR";
    public RecorderCaptureOptions Options { get; set; } = new();
    public List<string> Origins { get; set; } = [];
    public List<RecorderTab> Tabs { get; set; } = [];
    public List<RecorderFrame> Frames { get; set; } = [];
    public int EventCount { get; set; }
    public List<RecorderAssociation> Associations { get; set; } = [];
    public List<string> AcceptedPrivacyNotices { get; set; } = [];
}

public sealed class RecorderCaptureOptions
{
    public bool CaptureScreenshots { get; set; } = true;
    public bool CaptureSecrets { get; set; }
    public bool IncludeUploads { get; set; }
}

public sealed class RecorderTab
{
    public string Id { get; set; } = string.Empty;
    public string? OpenerId { get; set; }
    public string InitialUrl { get; set; } = string.Empty;
}

public sealed class RecorderFrame
{
    public string Id { get; set; } = string.Empty;
    public string TabId { get; set; } = string.Empty;
    public string? ParentFrameId { get; set; }
    public string Url { get; set; } = string.Empty;
    public bool Accessible { get; set; }
}

public sealed class RecorderAssociation
{
    public string EventId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string? LocatorId { get; set; }
    public string? EvidenceId { get; set; }
}

public sealed class RecorderEvidenceDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<RecorderEvidenceItem> Items { get; set; } = [];
}

public sealed class RecorderEvidenceItem
{
    public string Id { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public RecorderEvidenceKind Kind { get; set; }
    public string Path { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/webp";
    public int Width { get; set; }
    public int Height { get; set; }
    public long ByteLength { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string? Comment { get; set; }
    public List<RecorderEvidenceMask> Masks { get; set; } = [];
}

public sealed class RecorderEvidenceMask
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public RecorderMaskReason Reason { get; set; }
}

public sealed class RecorderIssuesDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<RecorderIssue> Issues { get; set; } = [];
}

public sealed class RecorderIssue
{
    public string Id { get; set; } = string.Empty;
    public RecorderIssueCode Code { get; set; }
    public RecorderIssueSeverity Severity { get; set; }
    public string? EventId { get; set; }
    public string? ActionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TechnicalDetail { get; set; } = string.Empty;
    public List<string> EvidenceIds { get; set; } = [];
    public List<RecorderResolutionOption> ResolutionOptions { get; set; } = [];
    public bool OmittedFromFlow { get; set; }
    public bool Resolved { get; set; }
}

public sealed class RecorderIntegrityDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<RecorderIntegrityEntry> Entries { get; set; } = [];
}

public sealed class RecorderIntegrityEntry
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
}

public sealed class RecorderNormalizedEvent
{
    public string Id { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public string TabId { get; set; } = string.Empty;
    public string FrameId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ActionId { get; set; }
    public string? LocatorId { get; set; }
    public bool Sensitive { get; set; }
    public JsonObject Data { get; set; } = [];
}

public sealed class RecorderComment
{
    public string Id { get; set; } = string.Empty;
    public string? ActionId { get; set; }
    public string Text { get; set; } = string.Empty;
}

public enum RecorderSessionState { Idle, Recording, Paused, Finalizing, Completed, Failed }
public enum RecorderEvidenceKind { Before, After }
public enum RecorderMaskReason
{
    Password,
    [JsonStringEnumMemberName("sensitive-field")]
    SensitiveField,
    [JsonStringEnumMemberName("user-region")]
    UserRegion
}
public enum RecorderIssueSeverity { Blocking, Warning, Info }
public enum RecorderResolutionOption
{
    Map,
    Omit,
    Confirm,
    [JsonStringEnumMemberName("provide-permission")]
    ProvidePermission,
    Attach,
    Discard
}

public enum RecorderIssueCode
{
    [JsonStringEnumMemberName("UNSUPPORTED_CLOSED_SHADOW_ROOT")]
    UnsupportedClosedShadowRoot,
    [JsonStringEnumMemberName("AMBIGUOUS_TARGET")]
    AmbiguousTarget,
    [JsonStringEnumMemberName("CROSS_ORIGIN_FRAME_NOT_CAPTURED")]
    CrossOriginFrameNotCaptured,
    [JsonStringEnumMemberName("POPUP_RELATION_UNCERTAIN")]
    PopupRelationUncertain,
    [JsonStringEnumMemberName("FILE_NOT_INCLUDED")]
    FileNotIncluded,
    [JsonStringEnumMemberName("SECRET_NOT_CAPTURED")]
    SecretNotCaptured,
    [JsonStringEnumMemberName("NAVIGATION_WITH_UNSAFE_QUERY")]
    NavigationWithUnsafeQuery,
    [JsonStringEnumMemberName("CUSTOM_WIDGET_REQUIRES_REVIEW")]
    CustomWidgetRequiresReview,
    [JsonStringEnumMemberName("UNSUPPORTED_INTERACTION")]
    UnsupportedInteraction
}
