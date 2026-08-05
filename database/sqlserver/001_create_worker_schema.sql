:setvar RpaSchema "rpa"
:setvar WorkItemsTable "WorkItem"
:setvar ExecutionsTable "Execution"
:setvar OutputsTable "ExecutionOutput"
:setvar ArtifactsTable "Artifact"
:setvar EventsTable "ExecutionEvent"

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'$(RpaSchema)') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA [$(RpaSchema)] AUTHORIZATION [dbo];');
END;

IF OBJECT_ID(N'[$(RpaSchema)].[$(WorkItemsTable)]', N'U') IS NULL
BEGIN
    CREATE TABLE [$(RpaSchema)].[$(WorkItemsTable)]
    (
        WorkItemId uniqueidentifier NOT NULL
            CONSTRAINT PK_Rpa_WorkItem PRIMARY KEY
            CONSTRAINT DF_Rpa_WorkItem_Id DEFAULT NEWSEQUENTIALID(),
        RpaCode nvarchar(100) NOT NULL,
        BatchId nvarchar(100) NULL,
        SessionKey nvarchar(200) NULL,
        Priority int NOT NULL CONSTRAINT DF_Rpa_WorkItem_Priority DEFAULT (0),
        Status nvarchar(30) NOT NULL CONSTRAINT DF_Rpa_WorkItem_Status DEFAULT (N'Pending'),
        AvailableAtUtc datetime2(3) NOT NULL
            CONSTRAINT DF_Rpa_WorkItem_Available DEFAULT SYSUTCDATETIME(),
        AttemptCount int NOT NULL CONSTRAINT DF_Rpa_WorkItem_Attempts DEFAULT (0),
        MaxAttempts int NOT NULL CONSTRAINT DF_Rpa_WorkItem_MaxAttempts DEFAULT (3),
        LeaseOwner nvarchar(200) NULL,
        LeaseExpiresAtUtc datetime2(3) NULL,
        InputJson nvarchar(max) NOT NULL CONSTRAINT DF_Rpa_WorkItem_Input DEFAULT (N'{}'),
        ConfigurationJson nvarchar(max) NOT NULL
            CONSTRAINT DF_Rpa_WorkItem_Configuration DEFAULT (N'{}'),
        AttachmentsJson nvarchar(max) NOT NULL
            CONSTRAINT DF_Rpa_WorkItem_Attachments DEFAULT (N'{}'),
        OutputJson nvarchar(max) NULL,
        ErrorType nvarchar(200) NULL,
        ErrorMessage nvarchar(2000) NULL,
        CreatedAtUtc datetime2(3) NOT NULL
            CONSTRAINT DF_Rpa_WorkItem_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL
            CONSTRAINT DF_Rpa_WorkItem_Updated DEFAULT SYSUTCDATETIME(),
        CompletedAtUtc datetime2(3) NULL,
        CONSTRAINT CK_Rpa_WorkItem_Status CHECK
            (Status IN (N'Pending', N'Retry', N'Running', N'Succeeded', N'Validated', N'Failed', N'Cancelled')),
        CONSTRAINT CK_Rpa_WorkItem_Attempts CHECK
            (AttemptCount >= 0 AND MaxAttempts BETWEEN 1 AND 100),
        CONSTRAINT CK_Rpa_WorkItem_InputJson CHECK (ISJSON(InputJson) = 1),
        CONSTRAINT CK_Rpa_WorkItem_ConfigurationJson CHECK (ISJSON(ConfigurationJson) = 1),
        CONSTRAINT CK_Rpa_WorkItem_AttachmentsJson CHECK (ISJSON(AttachmentsJson) = 1),
        CONSTRAINT CK_Rpa_WorkItem_OutputJson CHECK
            (OutputJson IS NULL OR ISJSON(OutputJson) = 1)
    );

    CREATE INDEX IX_Rpa_WorkItem_Claim
        ON [$(RpaSchema)].[$(WorkItemsTable)]
            (Status, AvailableAtUtc, Priority DESC, CreatedAtUtc)
        INCLUDE (RpaCode, LeaseExpiresAtUtc, AttemptCount, MaxAttempts);
END;

IF OBJECT_ID(N'[$(RpaSchema)].[$(ExecutionsTable)]', N'U') IS NULL
BEGIN
    CREATE TABLE [$(RpaSchema)].[$(ExecutionsTable)]
    (
        ExecutionId nvarchar(64) NOT NULL CONSTRAINT PK_Rpa_Execution PRIMARY KEY,
        WorkItemId uniqueidentifier NOT NULL,
        WorkerId nvarchar(200) NOT NULL,
        Status nvarchar(30) NOT NULL,
        StartedAtUtc datetime2(3) NOT NULL,
        CompletedAtUtc datetime2(3) NULL,
        ExecutedActions int NULL,
        OutputJson nvarchar(max) NULL,
        ErrorType nvarchar(200) NULL,
        ErrorMessage nvarchar(2000) NULL,
        CONSTRAINT FK_Rpa_Execution_WorkItem FOREIGN KEY (WorkItemId)
            REFERENCES [$(RpaSchema)].[$(WorkItemsTable)] (WorkItemId),
        CONSTRAINT CK_Rpa_Execution_Status CHECK
            (Status IN (N'Running', N'Succeeded', N'Validated', N'Failed', N'Cancelled')),
        CONSTRAINT CK_Rpa_Execution_OutputJson CHECK
            (OutputJson IS NULL OR ISJSON(OutputJson) = 1)
    );

    CREATE INDEX IX_Rpa_Execution_WorkItem
        ON [$(RpaSchema)].[$(ExecutionsTable)] (WorkItemId, StartedAtUtc DESC);
END;

IF OBJECT_ID(N'[$(RpaSchema)].[$(OutputsTable)]', N'U') IS NULL
BEGIN
    CREATE TABLE [$(RpaSchema)].[$(OutputsTable)]
    (
        ExecutionOutputId bigint IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_Rpa_ExecutionOutput PRIMARY KEY,
        ExecutionId nvarchar(64) NOT NULL,
        WorkItemId uniqueidentifier NOT NULL,
        Name nvarchar(200) NOT NULL,
        JsonValue nvarchar(max) NOT NULL,
        Sensitive bit NOT NULL CONSTRAINT DF_Rpa_Output_Sensitive DEFAULT (0),
        CreatedAtUtc datetime2(3) NOT NULL,
        CONSTRAINT FK_Rpa_Output_Execution FOREIGN KEY (ExecutionId)
            REFERENCES [$(RpaSchema)].[$(ExecutionsTable)] (ExecutionId),
        CONSTRAINT CK_Rpa_Output_Json CHECK (ISJSON(N'[' + JsonValue + N']') = 1)
    );

    CREATE UNIQUE INDEX UX_Rpa_Output_Execution_Name
        ON [$(RpaSchema)].[$(OutputsTable)] (ExecutionId, Name);
END;

IF OBJECT_ID(N'[$(RpaSchema)].[$(ArtifactsTable)]', N'U') IS NULL
BEGIN
    CREATE TABLE [$(RpaSchema)].[$(ArtifactsTable)]
    (
        ArtifactId bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Rpa_Artifact PRIMARY KEY,
        ExecutionId nvarchar(64) NOT NULL,
        WorkItemId uniqueidentifier NOT NULL,
        Name nvarchar(200) NOT NULL,
        Kind nvarchar(100) NOT NULL,
        Path nvarchar(2000) NOT NULL,
        SizeBytes bigint NOT NULL,
        Sha256 char(64) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL,
        CONSTRAINT FK_Rpa_Artifact_Execution FOREIGN KEY (ExecutionId)
            REFERENCES [$(RpaSchema)].[$(ExecutionsTable)] (ExecutionId),
        CONSTRAINT CK_Rpa_Artifact_Size CHECK (SizeBytes >= 0)
    );

    CREATE UNIQUE INDEX UX_Rpa_Artifact_Execution_Name
        ON [$(RpaSchema)].[$(ArtifactsTable)] (ExecutionId, Name);
END;

IF OBJECT_ID(N'[$(RpaSchema)].[$(EventsTable)]', N'U') IS NULL
BEGIN
    CREATE TABLE [$(RpaSchema)].[$(EventsTable)]
    (
        ExecutionEventId bigint IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_Rpa_ExecutionEvent PRIMARY KEY,
        ExecutionId nvarchar(64) NOT NULL,
        WorkItemId nvarchar(64) NULL,
        Kind nvarchar(100) NOT NULL,
        ActionId nvarchar(200) NULL,
        ActionName nvarchar(500) NULL,
        ActionType nvarchar(100) NULL,
        ExecutedActions int NULL,
        ElapsedMilliseconds bigint NULL,
        FailureCategory nvarchar(100) NULL,
        Retryable bit NULL,
        OccurredAtUtc datetime2(3) NOT NULL,
        CONSTRAINT FK_Rpa_Event_Execution FOREIGN KEY (ExecutionId)
            REFERENCES [$(RpaSchema)].[$(ExecutionsTable)] (ExecutionId)
    );

    CREATE INDEX IX_Rpa_Event_Execution
        ON [$(RpaSchema)].[$(EventsTable)] (ExecutionId, OccurredAtUtc);
END;
