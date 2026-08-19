-- Dirty read demo — Session B (the writer).
-- Opens a transaction, writes an uncommitted change, holds it open for 5
-- seconds so Session A has a window to read it, then rolls back.
-- Run concurrently with DirtyRead_SessionA.sql (not sequentially).

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRAN;

UPDATE dbo.EventLog
SET Payload = 'DIRTY-UNCOMMITTED'
WHERE Id = 1;

WAITFOR DELAY '00:00:05';

ROLLBACK TRAN;

-- After rollback, Id = 1's Payload is back to its original value — whatever
-- Session A read at the 2-second mark was never actually committed.
SELECT Id, Payload FROM dbo.EventLog WHERE Id = 1;
