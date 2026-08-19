-- Phantom read demo — Session B (the writer).
-- Waits 2 seconds so Session A's first COUNT(*) has already run, then
-- inserts a new UserId = 9999 row and auto-commits (no explicit
-- transaction) while Session A is mid-WAITFOR.
-- Run concurrently with PhantomRead_SessionA.sql (not sequentially).

WAITFOR DELAY '00:00:02';

INSERT INTO dbo.EventLog (EventType, UserId, CreatedAtUtc, Payload)
VALUES ('Login', 9999, SYSUTCDATETIME(), 'phantom-row'); -- auto-commits
