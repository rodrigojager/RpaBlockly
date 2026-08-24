:setvar RpaSchema "rpa"
:setvar ExecutionsTable "Execution"

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'[$(RpaSchema)].[$(ExecutionsTable)]', N'RpaPackageOrigin') IS NULL
BEGIN
    ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)]
        ADD RpaPackageOrigin nvarchar(100) NULL,
            RpaPackageRevision char(64) NULL,
            RpaPackageHash char(64) NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_Rpa_Execution_PackageIdentity'
)
BEGIN
    ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)]
        ADD CONSTRAINT CK_Rpa_Execution_PackageIdentity CHECK
        (
            (RpaPackageOrigin IS NULL AND RpaPackageRevision IS NULL AND RpaPackageHash IS NULL)
            OR
            (RpaPackageOrigin IS NOT NULL AND LEN(RpaPackageRevision) = 64
             AND LEN(RpaPackageHash) = 64)
        );
END;
