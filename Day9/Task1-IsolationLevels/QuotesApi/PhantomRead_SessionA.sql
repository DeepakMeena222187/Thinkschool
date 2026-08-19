-- Phantom read demo — Session A (the reader), anomaly variant.
-- READ COMMITTED locks (or, under RCSI, snapshots) rows already matched by
-- the predicate, but does nothing to stop new rows from being inserted
-- that also match the predicate, so Session B's committed INSERT in
-- between the two COUNT(*)s shows up in the second one.
-- Run concurrently with PhantomRead_SessionB.sql (not sequentially).

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRAN;

SELECT COUNT(*) FROM dbo.EventLog WHERE UserId = 9999;

WAITFOR DELAY '00:00:05';

-- Expected: count one higher than the first COUNT(*), because Session B's
-- INSERT committed while this transaction was waiting. This is the anomaly
-- (a "phantom" row appearing in a re-run of the same predicate).
SELECT COUNT(*) FROM dbo.EventLog WHERE UserId = 9999;

COMMIT TRAN;
