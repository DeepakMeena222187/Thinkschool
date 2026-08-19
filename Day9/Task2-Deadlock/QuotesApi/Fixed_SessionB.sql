-- Deadlock fix demo — Session B.
-- Identical to Deadlock_SessionB.sql except this session also locks
-- Id = 1 before Id = 2 — the same order Session A uses. By the time this
-- session reaches Id = 1, Session A is already holding it, so this
-- session simply waits (a normal lock wait) instead of forming a cycle.
-- Run concurrently with Fixed_SessionA.sql (not sequentially).

WAITFOR DELAY '00:00:01';

BEGIN TRAN;

UPDATE dbo.EventLog SET Payload = 'LockedByB-Row1' WHERE Id = 1;

WAITFOR DELAY '00:00:03';

UPDATE dbo.EventLog SET Payload = 'LockedByB-Row2' WHERE Id = 2;

COMMIT TRAN;
