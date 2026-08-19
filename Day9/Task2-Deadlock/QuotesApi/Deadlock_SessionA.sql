-- Deadlock demo — Session A.
-- Locks Id = 1 first, waits 3 seconds (long enough for Session B to grab
-- Id = 2 in the meantime), then reaches for Id = 2 — which Session B is
-- now holding. Session B, symmetrically, reaches for Id = 1 — which this
-- session is holding. Neither can proceed: SQL Server's deadlock monitor
-- detects the cycle and kills one session with error 1205.
-- Run concurrently with Deadlock_SessionB.sql (not sequentially).

BEGIN TRAN;

UPDATE dbo.EventLog SET Payload = 'LockedByA-Row1' WHERE Id = 1;

WAITFOR DELAY '00:00:03';

-- Session B holds an exclusive lock on Id = 2 by now — this blocks until
-- either B commits/rolls back, or the deadlock monitor kills one of us.
UPDATE dbo.EventLog SET Payload = 'LockedByA-Row2' WHERE Id = 2;

COMMIT TRAN;
