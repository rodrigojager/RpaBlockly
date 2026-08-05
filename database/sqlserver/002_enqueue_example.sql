:setvar RpaSchema "rpa"
:setvar WorkItemsTable "WorkItem"

INSERT INTO [$(RpaSchema)].[$(WorkItemsTable)]
    (RpaCode, BatchId, SessionKey, Priority, InputJson, ConfigurationJson, AttachmentsJson)
VALUES
    (
        N'exemplo',
        N'lote-didatico-001',
        N'usuario-demonstracao',
        0,
        N'{"Url":"https://example.invalid/"}',
        N'{}',
        N'{}'
    );

SELECT TOP (1) *
FROM [$(RpaSchema)].[$(WorkItemsTable)]
ORDER BY CreatedAtUtc DESC;
