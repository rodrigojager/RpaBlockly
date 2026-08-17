:setvar RpaSchema "rpa"

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'$(RpaSchema)') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA [$(RpaSchema)] AUTHORIZATION [dbo];');
END;

IF OBJECT_ID(N'[$(RpaSchema)].[RpaPackageRevision]', N'U') IS NULL
BEGIN
    CREATE TABLE [$(RpaSchema)].[RpaPackageRevision]
    (
        RpaId nvarchar(200) NOT NULL,
        Revision char(64) NOT NULL,
        ContentHash char(64) NOT NULL,
        OriginKind nvarchar(100) NOT NULL,
        OriginLocation nvarchar(1000) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL
            CONSTRAINT DF_RpaPackageRevision_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_RpaPackageRevision PRIMARY KEY (RpaId, Revision),
        CONSTRAINT CK_RpaPackageRevision_Hash CHECK
            (Revision = ContentHash AND LEN(ContentHash) = 64)
    );
END;

IF OBJECT_ID(N'[$(RpaSchema)].[RpaPackageDocument]', N'U') IS NULL
BEGIN
    CREATE TABLE [$(RpaSchema)].[RpaPackageDocument]
    (
        RpaId nvarchar(200) NOT NULL,
        Revision char(64) NOT NULL,
        DocumentName nvarchar(40) NOT NULL,
        JsonContent nvarchar(max) NOT NULL,
        Sha256 char(64) NOT NULL,
        CONSTRAINT PK_RpaPackageDocument
            PRIMARY KEY (RpaId, Revision, DocumentName),
        CONSTRAINT FK_RpaPackageDocument_Revision
            FOREIGN KEY (RpaId, Revision)
            REFERENCES [$(RpaSchema)].[RpaPackageRevision] (RpaId, Revision),
        CONSTRAINT CK_RpaPackageDocument_Name CHECK
            (DocumentName IN
                (N'flow.production.json', N'locators.production.json', N'rpa.policy.json')),
        CONSTRAINT CK_RpaPackageDocument_Json CHECK (ISJSON(JsonContent) = 1)
    );
END;

IF OBJECT_ID(N'[$(RpaSchema)].[RpaPackageCurrent]', N'U') IS NULL
BEGIN
    CREATE TABLE [$(RpaSchema)].[RpaPackageCurrent]
    (
        RpaId nvarchar(200) NOT NULL CONSTRAINT PK_RpaPackageCurrent PRIMARY KEY,
        Revision char(64) NOT NULL,
        UpdatedAtUtc datetime2(3) NOT NULL
            CONSTRAINT DF_RpaPackageCurrent_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_RpaPackageCurrent_Revision
            FOREIGN KEY (RpaId, Revision)
            REFERENCES [$(RpaSchema)].[RpaPackageRevision] (RpaId, Revision)
    );
END;
