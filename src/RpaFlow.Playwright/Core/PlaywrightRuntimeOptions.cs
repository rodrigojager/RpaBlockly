namespace RpaFlow.Playwright;

public sealed record PlaywrightRuntimeOptions(
    bool Headless,
    string Browser,
    int ActionTimeoutSeconds,
    int UploadTimeoutSeconds,
    string OutputDirectory,
    string ConfigurationDirectory,
    string Locale = "pt-BR",
    int ViewportWidth = 1440,
    int ViewportHeight = 1000,
    string? StorageStatePath = null,
    bool SaveStorageState = false,
    int ReadinessQuietPeriodMs = 800,
    int FormStabilityMs = 600,
    IReadOnlyList<string>? BusySelectors = null,
    bool HoldBrowserOpenForInspection = false,
    long MaximumArtifactBytes = 50 * 1024 * 1024,
    int MaximumArtifactFilesPerExecution = 100,
    int ArtifactRetentionDays = 30,
    bool CaptureScreenshotsAfterActions = false);
