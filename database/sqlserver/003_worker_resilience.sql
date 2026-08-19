:setvar RpaSchema "rpa"
:setvar WorkersTable "WorkerState"

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'$(RpaSchema)') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA [$(RpaSchema)] AUTHORIZATION [dbo];');
END;

IF OBJECT_ID(N'[$(RpaSchema)].[$(WorkersTable)]', N'U') IS NULL
BEGIN
    CREATE TABLE [$(RpaSchema)].[$(WorkersTable)]
    (
        InstanceId uniqueidentifier NOT NULL CONSTRAINT PK_Rpa_WorkerState PRIMARY KEY,
        WorkerId nvarchar(200) NOT NULL,
        HostName nvarchar(200) NOT NULL,
        ProcessId int NOT NULL,
        Status nvarchar(50) NOT NULL,
        Ready bit NOT NULL,
        AcceptingClaims bit NOT NULL,
        ExecutionEnabled bit NOT NULL,
        LeadershipAcquired bit NOT NULL,
        PollingHealthy bit NOT NULL,
        ActiveExecutions int NOT NULL,
        MaximumParallelism int NOT NULL,
        AvailableExecutionSlots int NOT NULL,
        StartedAtUtc datetime2(3) NOT NULL,
        LeadershipHeartbeatAtUtc datetime2(3) NULL,
        PollingHeartbeatAtUtc datetime2(3) NULL,
        LastPollingSuccessAtUtc datetime2(3) NULL,
        NextPollingAtUtc datetime2(3) NULL,
        LastFailureAtUtc datetime2(3) NULL,
        LastFailureType nvarchar(200) NULL,
        Finalized bit NOT NULL CONSTRAINT DF_Rpa_WorkerState_Finalized DEFAULT (0),
        FinalizedAtUtc datetime2(3) NULL,
        UpdatedAtUtc datetime2(3) NOT NULL,
        CONSTRAINT CK_Rpa_WorkerState_Capacity CHECK
            (ActiveExecutions >= 0 AND MaximumParallelism >= 0 AND AvailableExecutionSlots >= 0)
    );

    CREATE INDEX IX_Rpa_WorkerState_Updated
        ON [$(RpaSchema)].[$(WorkersTable)] (UpdatedAtUtc DESC);
END;
