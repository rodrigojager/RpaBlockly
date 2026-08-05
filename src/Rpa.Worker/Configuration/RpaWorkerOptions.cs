namespace Rpa.Worker.Configuration;

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
}

public sealed class RpaDefinitionOptions
{
    public bool Enabled { get; set; } = true;

    public bool ClaimEnabled { get; set; }

    public string FlowFile { get; set; } = string.Empty;

    public string? ConfigurationFile { get; set; }

    public RpaRuntimeOptions Runtime { get; set; } = new();

    public List<string> IrreversibleActionIds { get; set; } = [];

    public List<OutputMappingOptions> Outputs { get; set; } = [];

    public List<ArtifactMappingOptions> Artifacts { get; set; } = [];
}

public sealed class RpaRuntimeOptions
{
    public bool Headless { get; set; } = true;

    public string Browser { get; set; } = "cloakbrowser";

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
