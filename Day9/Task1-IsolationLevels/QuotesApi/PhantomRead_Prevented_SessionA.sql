-- Phantom read demo — Session A (the reader), prevented variant.
-- Identical to PhantomRead_SessionA.sql except for the isolation level:
-- SERIALIZABLE takes a range (key-range) lock covering UserId = 9999,
-- which blocks Session B's INSERT of a new UserId = 9999 row until this
-- transaction commits, instead of letting it commit in between.
-- Run concurrently with PhantomRead_SessionB.sql (not sequentially).

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRAN;

SELECT COUNT(*) FROM dbo.EventLog WHERE UserId = 9999;

WAITFOR DELAY '00:00:05';

-- Expected: the same count as the first COUNT(*). Session B's INSERT was
-- blocked by this transaction's range lock on UserId = 9999 and only
-- proceeds (and Session B's own script only returns) after COMMIT TRAN
-- below runs.
SELECT COUNT(*) FROM dbo.EventLog WHERE UserId = 9999;

COMMIT TRAN;
