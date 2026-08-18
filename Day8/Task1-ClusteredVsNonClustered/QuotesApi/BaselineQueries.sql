-- These queries run BEFORE any non-clustered index exists on dbo.EventLog.
-- At this point the only index is the implicit clustered index created by
-- the IDENTITY PRIMARY KEY on Id, so both queries must scan/probe by Id order,
-- with no supporting index on EventType or (UserId, CreatedAtUtc).

-- Query A: single-column equality predicate on EventType.
SET STATISTICS IO ON;
SELECT * FROM dbo.EventLog WHERE EventType = 'Login';

-- Query B: equality on UserId combined with a range predicate on CreatedAtUtc.
SET STATISTICS IO ON;
SELECT * FROM dbo.EventLog WHERE UserId = 2500 AND CreatedAtUtc > DATEADD(day, -30, GETUTCDATE());
