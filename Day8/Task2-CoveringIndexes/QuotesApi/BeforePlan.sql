-- With only IX_EventLog_UserId_CreatedAtUtc (UserId, CreatedAtUtc) in place,
-- this query's SELECT list also needs EventType and Payload, neither of
-- which is in that index. That forces a key lookup back into the clustered
-- index for every row matched by the UserId seek, to fetch EventType and
-- Payload. Expect an Index Seek + Key Lookup pair joined by Nested Loops,
-- not a plan servable from the non-clustered index alone.
--
-- The query runs twice below: once under STATISTICS IO to capture the
-- actual logical reads, and once under SHOWPLAN_TEXT to capture the plan
-- shape. SQL Server requires SET SHOWPLAN_TEXT ON to be the only statement
-- in its batch, so it can't share a batch (or a single run) with
-- STATISTICS IO -- hence the two separate passes.
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
