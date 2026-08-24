:setvar RpaSchema "rpa"
:setvar EventsTable "ExecutionEvent"

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'[$(RpaSchema)].[$(EventsTable)]', N'RpaId') IS NULL
BEGIN
    ALTER TABLE [$(RpaSchema)].[$(EventsTable)]
        ADD RpaId nvarchar(200) NULL,
            PackageOrigin nvarchar(100) NULL,
            PackageRevision char(64) NULL,
            PackageHash char(64) NULL,
            LocatorId nvarchar(200) NULL,
            CandidateId nvarchar(200) NULL,
            ResolutionReason nvarchar(100) NULL,
            Detail nvarchar(500) NULL;
END;
