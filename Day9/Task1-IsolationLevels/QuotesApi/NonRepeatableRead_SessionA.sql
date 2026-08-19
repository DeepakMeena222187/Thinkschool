-- Non-repeatable read demo — Session A (the reader), anomaly variant.
-- READ COMMITTED only holds shared locks for the instant of each read, not
-- for the life of the transaction, so Session B's committed UPDATE in
-- between the two SELECTs is visible to the second one.
-- Run concurrently with NonRepeatableRead_SessionB.sql (not sequentially).

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRAN;

SELECT Id, Payload FROM dbo.EventLog WHERE Id = 2;

WAITFOR DELAY '00:00:05';

-- Expected: a different Payload than the first SELECT, because Session B's
-- UPDATE committed while this transaction was waiting. This is the anomaly.
SELECT Id, Payload FROM dbo.EventLog WHERE Id = 2;

COMMIT TRAN;
