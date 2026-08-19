-- Dirty read demo — Session A (the reader).
-- Waits 2 seconds so Session B's UPDATE is in flight but not yet committed,
-- then reads under READ UNCOMMITTED — which reads the uncommitted value
-- straight off the row, ignoring Session B's exclusive lock.
-- Run concurrently with DirtyRead_SessionB.sql (not sequentially).

WAITFOR DELAY '00:00:02';

SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- Expected: 'DIRTY-UNCOMMITTED', even though Session B rolls back afterward.
-- This is the anomaly — Session A observed a value that never committed.
SELECT Id, Payload FROM dbo.EventLog WHERE Id = 1;
