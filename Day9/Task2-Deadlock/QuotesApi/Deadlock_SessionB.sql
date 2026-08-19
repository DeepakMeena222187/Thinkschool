-- Deadlock demo — Session B.
-- Starts 1 second after Session A so Session A's first UPDATE (on Id = 1)
-- lands first. This session then locks Id = 2, waits 3 seconds, and
-- reaches for Id = 1 — which Session A is holding, while Session A is
-- simultaneously reaching for the row this session holds. That circular
-- wait is exactly what forces SQL Server's deadlock monitor to pick a
-- victim and kill one session with error 1205.
-- Run concurrently with Deadlock_SessionA.sql (not sequentially).

WAITFOR DELAY '00:00:01';

BEGIN TRAN;

UPDATE dbo.EventLog SET Payload = 'LockedByB-Row2' WHERE Id = 2;

WAITFOR DELAY '00:00:03';

-- Session A holds an exclusive lock on Id = 1 by now — this blocks until
-- either A commits/rolls back, or the deadlock monitor kills one of us.
UPDATE dbo.EventLog SET Payload = 'LockedByB-Row1' WHERE Id = 1;

COMMIT TRAN;
