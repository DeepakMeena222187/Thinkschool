-- Non-repeatable read demo — Session B (the writer), prevented variant.
-- Identical to NonRepeatableRead_SessionB.sql. Under REPEATABLE READ on
-- Session A's side, this UPDATE will block on Session A's shared lock
-- until Session A's COMMIT TRAN runs, instead of committing immediately.
-- Run concurrently with NonRepeatableRead_Prevented_SessionA.sql (not
-- sequentially).

WAITFOR DELAY '00:00:02';

UPDATE dbo.EventLog SET Payload = 'CHANGED-BY-B' WHERE Id = 2; -- blocks until Session A commits
