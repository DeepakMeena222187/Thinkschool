-- Same two queries as BaselineQueries.sql, run AFTER IX_EventLog_EventType and
-- IX_EventLog_UserId_CreatedAtUtc exist (see CreateIndexes.sql), so the
-- logical-reads numbers from STATISTICS IO are directly comparable before/after.

-- Query A: single-column equality predicate on EventType.
SET STATISTICS IO ON;
SELECT * FROM dbo.EventLog WHERE EventType = 'Login';

-- Query B: equality on UserId combined with a range predicate on CreatedAtUtc.
SET STATISTICS IO ON;
SELECT * FROM dbo.EventLog WHERE UserId = 2500 AND CreatedAtUtc > DATEADD(day, -30, GETUTCDATE());
