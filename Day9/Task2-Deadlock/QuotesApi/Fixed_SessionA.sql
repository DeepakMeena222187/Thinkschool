-- Deadlock fix demo — Session A.
-- Identical to Deadlock_SessionA.sql except both sessions now touch rows
-- in the same order: Id = 1 before Id = 2. With consistent lock ordering
-- there is no row for the other session to hold that this session doesn't
-- already hold first — no circular wait is possible, so this can only
-- ever block, never deadlock.
-- Run concurrently with Fixed_SessionB.sql (not sequentially).

BEGIN TRAN;

UPDATE dbo.EventLog SET Payload = 'LockedByA-Row1' WHERE Id = 1;

WAITFOR DELAY '00:00:03';

UPDATE dbo.EventLog SET Payload = 'LockedByA-Row2' WHERE Id = 2;

COMMIT TRAN;
