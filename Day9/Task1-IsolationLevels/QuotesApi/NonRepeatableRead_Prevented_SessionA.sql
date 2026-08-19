-- Non-repeatable read demo — Session A (the reader), prevented variant.
-- Identical to NonRepeatableRead_SessionA.sql except for the isolation
-- level: REPEATABLE READ holds shared locks on every row read for the
-- life of the transaction, so Session B's UPDATE blocks until this
-- transaction commits (or rolls back) instead of committing in between.
-- Run concurrently with NonRepeatableRead_SessionB.sql (not sequentially).

SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

BEGIN TRAN;

SELECT Id, Payload FROM dbo.EventLog WHERE Id = 2;

WAITFOR DELAY '00:00:05';

-- Expected: the same Payload as the first SELECT. Session B's UPDATE was
-- blocked by this transaction's shared lock on Id = 2 and only proceeds
-- (and Session B's own script only returns) after COMMIT TRAN below runs.
SELECT Id, Payload FROM dbo.EventLog WHERE Id = 2;

COMMIT TRAN;
