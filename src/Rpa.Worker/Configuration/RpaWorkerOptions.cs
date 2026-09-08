namespace Rpa.Worker.Configuration;

using RpaFlow.Playwright;
using RpaFlow.Playwright.V2;

public sealed class RpaWorkerOptions
{
    public const string SectionName = "RpaWorker";

    public bool Enabled { get; set; }

    public WorkerExecutionMode ExecutionMode { get; set; } =
        WorkerExecutionMode.SafeValidation;

    public string WorkerId { get; set; } = Environment.MachineName;

    public string WorkspaceRoot { get; set; } = ".";

    public int PollIntervalSeconds { get; set; } = 5;

    public int MaxParallelism { get; set; } = 2;

    public int LeaseSeconds { get; set; } = 300;

    public int HeartbeatSeconds { get; set; } = 60;

    public int CaseTimeoutMinutes { get; set; } = 30;

    public int RetryDelaySeconds { get; set; } = 60;

    public int OperationalHeartbeatSeconds { get; set; } = 30;

    public string GlobalExecutionLockName { get; set; } = "Rpa.Worker.PollingGlobal";

    public WorkerStorageOptions Storage { get; set; } = new();

    public WorkerTableOptions Tables { get; set; } = new();

    public MicrosoftGraphEmailReaderOptions EmailReader { get; set; } = new();

    public Dictionary<string, RpaDefinitionOptions> Definitions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MicrosoftGraphEmailReaderOptions
{
    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public Dictionary<string, EmailOneTimeCodeProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EmailOneTimeCodeProviderOptions
{
    public const string MicrosoftGraphProvider = "MicrosoftGraph";

    public bool Enabled { get; set; }

    public string Provider { get; set; } = MicrosoftGraphProvider;

    public string Mailbox { get; set; } = string.Empty;

    public string? SenderAddress { get; set; }

    public string SubjectContains { get; set; } = string.Empty;

    public string CodePattern { get; set; } = string.Empty;

    public int MaximumEmailAgeMinutes { get; set; } = 5;

    public int RequestedEmailCount { get; set; } = 10;
}

public enum WorkerExecutionMode
{
    SafeValidation,
    Production
}

public sealed class WorkerStorageOptions
{
    public string ArtifactRoot { get; set; } = "storage/artifacts";

    public string SessionStateRoot { get; set; } = "storage/sessions";
}

public sealed class WorkerTableOptions
{
    public string Schema { get; set; } = "rpa";

    public string WorkItems { get; set; } = "WorkItem";

    public string Executions { get; set; } = "Execution";

    public string Outputs { get; set; } = "ExecutionOutput";

    public string Artifacts { get; set; } = "Artifact";

    public string Events { get; set; } = "ExecutionEvent";

    public string Workers { get; set; } = "WorkerState";
}

public sealed class RpaDefinitionOptions
{
    public bool Enabled { get; set; } = true;

    public bool ClaimEnabled { get; set; }

    public RpaPackageReferenceOptions? Package { get; set; }

    public string? ConfigurationFile { get; set; }

    public RpaRuntimeOptions Runtime { get; set; } = new();

    public string? SafeValidationBoundaryActionId { get; set; }

    public List<string> IrreversibleActionIds { get; set; } = [];

    public List<string> AuthenticationAttemptActionIds { get; set; } = [];

    public List<string> AuthenticationFailureActionIds { get; set; } = [];

    public List<string> MfaAttemptActionIds { get; set; } = [];

    public List<string> MfaFailureActionIds { get; set; } = [];

    public List<OutputMappingOptions> Outputs { get; set; } = [];

    public List<ArtifactMappingOptions> Artifacts { get; set; } = [];
}

public sealed class RpaPackageReferenceOptions
{
    public string RpaId { get; set; } = string.Empty;

    public string OriginName { get; set; } = "source";

    public string Provider { get; set; } = "File";

    public string Location { get; set; } = "packages";

    public string? Revision { get; set; }

    public RpaPackageStoreReferenceOptions? Overlay { get; set; }
}

public sealed class RpaPackageStoreReferenceOptions
{
    public string OriginName { get; set; } = "overlay";

    public string Provider { get; set; } = "File";

    public string Location { get; set; } = "packages-overlay";
}

public sealed class RpaRuntimeOptions
{
    public bool Headless { get; set; } = true;

    public string Browser { get; set; } = PlaywrightBrowserSelection.DefaultValue;

    public bool SpyBrowserHumanize { get; set; } = true;

    public int ActionTimeoutSeconds { get; set; } = 30;

    public int UploadTimeoutSeconds { get; set; } = 90;

    public int ReadinessQuietPeriodMs { get; set; } = 800;

    public int FormStabilityMs { get; set; } = 600;

    public string Locale { get; set; } = "pt-BR";

    public int ViewportWidth { get; set; } = 1440;

    public int ViewportHeight { get; set; } = 1000;

    public List<string>? BusySelectors { get; set; }

    public bool UseSessionState { get; set; }

    public bool SaveSessionState { get; set; }

    public long MaximumArtifactBytes { get; set; } = 50 * 1024 * 1024;

    public int MaximumArtifactFilesPerExecution { get; set; } = 100;

    public int ArtifactRetentionDays { get; set; } = 30;

    public RpaCaptchaWorkerOptions? Captcha { get; set; }
}

/// <summary>
/// Espelha <c>CaptchaOptions</c> do runtime de execução com binding amigável
/// de configuração (classes com setters).
/// </summary>
public sealed class RpaCaptchaWorkerOptions
{
    public string? ServiceUrl { get; set; }

    public string? ServiceApiKey { get; set; }

    public string? OcrModelPath { get; set; }

    public int ServiceTimeoutSeconds { get; set; } = 60;

    public int RecaptchaMaxAttempts { get; set; } = 3;

    public int HCaptchaMaxAttempts { get; set; } = 3;

    public int HumanHandoffTimeoutSeconds { get; set; } = 900;

    public int HumanHandoffPollSeconds { get; set; } = 2;

    public int DeadlineSeconds { get; set; } = 90;

    public int ServiceRetryAttempts { get; set; } = 2;

    public int ServiceRetryBackoffMs { get; set; } = 250;

    public int MaximumServiceResponseBytes { get; set; } = 1024 * 1024;

    public int MaximumImagePixels { get; set; } = 16_000_000;

    public double SliderMinimumScore { get; set; } = 0.65;

    public bool LocalOnly { get; set; } = true;

    public bool AutoSolveEnabled { get; set; }

    public bool AllowVlmFallback { get; set; }

    public int SamePageWaitSeconds { get; set; } = 30;

    public bool CloudflareSidecarEnabled { get; set; }

    public string CloudflareSidecarProvider { get; set; } = "byparr";

    public string? CloudflareSidecarUrl { get; set; }

    public string? CloudflareSidecarApiKey { get; set; }

    public int CloudflareSidecarTimeoutSeconds { get; set; } = 60;

    public int CloudflareSidecarMaximumResponseBytes { get; set; } = 1024 * 1024;

    public List<string>? CloudflareSidecarAllowedHosts { get; set; }

    public CaptchaOptions ToRuntimeOptions() =>
        new(
            ServiceUrl: ServiceUrl,
            ServiceApiKey: ServiceApiKey,
            OcrModelPath: OcrModelPath,
            ServiceTimeoutSeconds: ServiceTimeoutSeconds,
            RecaptchaMaxAttempts: RecaptchaMaxAttempts,
            HCaptchaMaxAttempts: HCaptchaMaxAttempts,
            HumanHandoffTimeoutSeconds: HumanHandoffTimeoutSeconds,
            HumanHandoffPollSeconds: HumanHandoffPollSeconds,
            DeadlineSeconds: DeadlineSeconds,
            ServiceRetryAttempts: ServiceRetryAttempts,
            ServiceRetryBackoffMs: ServiceRetryBackoffMs,
            MaximumServiceResponseBytes: MaximumServiceResponseBytes,
            MaximumImagePixels: MaximumImagePixels,
            SliderMinimumScore: SliderMinimumScore,
            LocalOnly: LocalOnly,
            AutoSolveEnabled: AutoSolveEnabled,
            AllowVlmFallback: AllowVlmFallback,
            SamePageWaitSeconds: SamePageWaitSeconds,
            CloudflareSidecarEnabled: CloudflareSidecarEnabled,
            CloudflareSidecarProvider: CloudflareSidecarProvider,
            CloudflareSidecarUrl: CloudflareSidecarUrl,
            CloudflareSidecarApiKey: CloudflareSidecarApiKey,
            CloudflareSidecarTimeoutSeconds: CloudflareSidecarTimeoutSeconds,
            CloudflareSidecarMaximumResponseBytes: CloudflareSidecarMaximumResponseBytes,
            CloudflareSidecarAllowedHosts: CloudflareSidecarAllowedHosts);
}

public sealed class OutputMappingOptions
{
    public string Name { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public bool Required { get; set; }

    public bool Sensitive { get; set; }
}

public sealed class ArtifactMappingOptions
{
    public string Name { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Kind { get; set; } = "file";

    public bool Required { get; set; }
}

public sealed record WorkerPaths(
    string ConfigurationDirectory,
    string WorkspaceRoot,
    string ArtifactRoot,
    string SessionStateRoot);
