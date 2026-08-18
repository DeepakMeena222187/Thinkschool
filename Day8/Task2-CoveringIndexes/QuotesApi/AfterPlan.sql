-- Same query as BeforePlan.sql. Now that IX_EventLog_UserId_Covering exists
-- (key: UserId, with EventType, CreatedAtUtc, Payload INCLUDEd), every
-- column in the SELECT list is present in the index itself. The query
-- should be servable entirely from the non-clustered index -- an Index
-- Seek with no Key Lookup back into the clustered index.
--
-- As in BeforePlan.sql, the query runs twice: once under STATISTICS IO for
-- actual logical reads, once under SHOWPLAN_TEXT for the plan shape --
-- SET SHOWPLAN_TEXT ON must be the only statement in its batch, so it can't
-- share a single run with STATISTICS IO.
SET STATISTICS IO ON;
GO

SELECT EventType, UserId, CreatedAtUtc, Payload
FROM dbo.EventLog
WHERE UserId = 2500;
GO

SET STATISTICS IO OFF;
GO

SET SHOWPLAN_TEXT ON;
GO

SELECT EventType, UserId, CreatedAtUtc, Payload
FROM dbo.EventLog
WHERE UserId = 2500;
GO

SET SHOWPLAN_TEXT OFF;
GO
