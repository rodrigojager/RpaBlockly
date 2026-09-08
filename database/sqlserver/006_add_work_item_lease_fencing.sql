:setvar RpaSchema "rpa"
:setvar WorkItemsTable "WorkItem"
:setvar ExecutionsTable "Execution"

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'[$(RpaSchema)].[$(WorkItemsTable)]', N'LeaseToken') IS NULL
BEGIN
    ALTER TABLE [$(RpaSchema)].[$(WorkItemsTable)]
        ADD LeaseToken uniqueidentifier NULL;
END;

IF COL_LENGTH(N'[$(RpaSchema)].[$(ExecutionsTable)]', N'LeaseToken') IS NULL
BEGIN
    ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)]
        ADD LeaseToken uniqueidentifier NULL;
END;

IF COL_LENGTH(N'[$(RpaSchema)].[$(ExecutionsTable)]', N'RecoveryPolicyJson') IS NULL
BEGIN
    ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)]
        ADD RecoveryPolicyJson nvarchar(max) NULL;
END;

EXEC(N'UPDATE [$(RpaSchema)].[$(ExecutionsTable)]
   SET RecoveryPolicyJson = N''{"policyKnown":false,"authenticationAttemptActionIds":[],"mfaAttemptActionIds":[],"irreversibleActionIds":[]}''
 WHERE RecoveryPolicyJson IS NULL;');

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[$(RpaSchema)].[$(ExecutionsTable)]')
      AND name = N'RecoveryPolicyJson'
      AND is_nullable = 1
)
BEGIN
    EXEC(N'ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)]
        ALTER COLUMN RecoveryPolicyJson nvarchar(max) NOT NULL;');
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'[$(RpaSchema)].[$(ExecutionsTable)]')
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'[$(RpaSchema)].[$(ExecutionsTable)]'),
          N'RecoveryPolicyJson',
          'ColumnId')
)
BEGIN
    EXEC(N'ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)]
        ADD CONSTRAINT DF_Rpa_Execution_RecoveryPolicy DEFAULT
        (N''{"policyKnown":false,"authenticationAttemptActionIds":[],"mfaAttemptActionIds":[],"irreversibleActionIds":[]}'' )
        FOR RecoveryPolicyJson;');
END;

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_Rpa_Execution_RecoveryPolicyJson'
      AND parent_object_id = OBJECT_ID(N'[$(RpaSchema)].[$(ExecutionsTable)]')
)
BEGIN
    ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)]
        DROP CONSTRAINT CK_Rpa_Execution_RecoveryPolicyJson;
END;

EXEC(N'ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)] WITH CHECK
    ADD CONSTRAINT CK_Rpa_Execution_RecoveryPolicyJson CHECK
    (ISJSON(RecoveryPolicyJson) = 1);');

EXEC(N'UPDATE [$(RpaSchema)].[$(WorkItemsTable)]
   SET LeaseOwner = NULL,
       LeaseToken = NULL,
       LeaseExpiresAtUtc = NULL
 WHERE Status <> N''Running''
   AND (LeaseOwner IS NOT NULL OR LeaseToken IS NOT NULL OR LeaseExpiresAtUtc IS NOT NULL);

UPDATE [$(RpaSchema)].[$(WorkItemsTable)]
   SET LeaseToken = NEWID()
 WHERE Status = N''Running''
   AND LeaseToken IS NULL;

UPDATE execution
   SET LeaseToken = CASE
       WHEN execution.Status = N''Running'' AND workItem.Status = N''Running''
           THEN workItem.LeaseToken
       ELSE NEWID()
   END
  FROM [$(RpaSchema)].[$(ExecutionsTable)] AS execution
  INNER JOIN [$(RpaSchema)].[$(WorkItemsTable)] AS workItem
          ON workItem.WorkItemId = execution.WorkItemId
 WHERE execution.LeaseToken IS NULL;');

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[$(RpaSchema)].[$(ExecutionsTable)]')
      AND name = N'LeaseToken'
      AND is_nullable = 1
)
BEGIN
    EXEC(N'ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)]
        ALTER COLUMN LeaseToken uniqueidentifier NOT NULL;');
END;

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_Rpa_WorkItem_LeaseIdentity'
      AND parent_object_id = OBJECT_ID(N'[$(RpaSchema)].[$(WorkItemsTable)]')
)
BEGIN
    ALTER TABLE [$(RpaSchema)].[$(WorkItemsTable)]
        DROP CONSTRAINT CK_Rpa_WorkItem_LeaseIdentity;
END;

EXEC(N'ALTER TABLE [$(RpaSchema)].[$(WorkItemsTable)] WITH CHECK
    ADD CONSTRAINT CK_Rpa_WorkItem_LeaseIdentity CHECK
    (
        (Status = N''Running'' AND LeaseOwner IS NOT NULL
         AND LeaseToken IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL)
        OR
        (Status <> N''Running'' AND LeaseOwner IS NULL
         AND LeaseToken IS NULL AND LeaseExpiresAtUtc IS NULL)
    );');

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_Rpa_Execution_PackageIdentity'
      AND parent_object_id = OBJECT_ID(N'[$(RpaSchema)].[$(ExecutionsTable)]')
)
BEGIN
    ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)]
        DROP CONSTRAINT CK_Rpa_Execution_PackageIdentity;
END;

IF COL_LENGTH(N'[$(RpaSchema)].[$(ExecutionsTable)]', N'RpaPackageOrigin') IS NOT NULL
BEGIN
    EXEC(N'ALTER TABLE [$(RpaSchema)].[$(ExecutionsTable)] WITH CHECK
        ADD CONSTRAINT CK_Rpa_Execution_PackageIdentity CHECK
        (
            (RpaPackageOrigin IS NULL AND RpaPackageRevision IS NULL AND RpaPackageHash IS NULL)
            OR
            (RpaPackageOrigin IS NOT NULL
             AND RpaPackageRevision IS NOT NULL
             AND RpaPackageHash IS NOT NULL
             AND LEN(RpaPackageRevision) = 64
             AND LEN(RpaPackageHash) = 64)
        );');
END;

COMMIT TRANSACTION;
