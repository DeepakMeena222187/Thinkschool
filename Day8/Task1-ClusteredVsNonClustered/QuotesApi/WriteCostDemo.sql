-- With IX_EventLog_EventType and IX_EventLog_UserId_CreatedAtUtc in place,
-- dbo.EventLog now has 3 structures to maintain on every write: the clustered
-- index (the table itself, keyed on Id) plus the 2 non-clustered indexes.
-- STATISTICS IO on this single-row INSERT shows the extra logical reads/writes
-- that come from keeping all 3 in sync, versus the 1-structure cost before
-- CreateIndexes.sql ran.
SET STATISTICS IO ON;

INSERT INTO dbo.EventLog (EventType, UserId, CreatedAtUtc, Payload)
VALUES ('Login', 4242, SYSUTCDATETIME(), 'Payload-writecost-demo');
