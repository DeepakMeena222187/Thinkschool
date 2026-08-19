-- Non-repeatable read demo — Session B (the writer).
-- Waits 2 seconds so Session A's first SELECT has already run, then updates
-- and auto-commits (no explicit transaction) while Session A is mid-WAITFOR.
-- Run concurrently with NonRepeatableRead_SessionA.sql (not sequentially).

WAITFOR DELAY '00:00:02';

UPDATE dbo.EventLog SET Payload = 'CHANGED-BY-B' WHERE Id = 2; -- auto-commits
