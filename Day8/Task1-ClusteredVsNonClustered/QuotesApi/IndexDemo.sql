-- Standalone table for the clustered-vs-non-clustered index exercise.
-- Not wired into the EF Core app, its DbContext, or any migration.
CREATE TABLE dbo.EventLog (
    Id INT IDENTITY PRIMARY KEY,
    EventType NVARCHAR(50) NOT NULL,
    UserId INT NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    Payload NVARCHAR(200) NOT NULL
);
GO

-- Set-based generation of ~100,000 rows (cross join instead of a WHILE loop,
-- since a WHILE loop would insert one row at a time and be far slower).
-- sys.all_objects cross-joined with itself produces far more than 100,000
-- combinations, so TOP (100000) trims it down to the target row count.
-- NEWID() is evaluated per row (SQL Server can't fold/cache a non-deterministic
-- function across rows), which is what makes each row's random values independent.
--
-- The EventType roll is computed in the derived table `g` and only READ (not
-- recomputed) by the outer CASE. Feeding a volatile expression like
-- ABS(CHECKSUM(NEWID())) % 5 directly as a simple CASE's input, instead of
-- through a derived table, would be a bug: SQL Server evaluates that
-- expression separately for each WHEN comparison rather than once, so each
-- branch check draws its own fresh NEWID() -- e.g. 'Error' (falling through
-- every WHEN) would end up at ~41% of rows instead of the intended ~20%,
-- with 'Purchase' skewed down to ~10%.
INSERT INTO dbo.EventLog (EventType, UserId, CreatedAtUtc, Payload)
SELECT
    CASE g.EventTypeRoll
        WHEN 0 THEN 'Login'
        WHEN 1 THEN 'Logout'
        WHEN 2 THEN 'PageView'
        WHEN 3 THEN 'Purchase'
        ELSE 'Error'
    END AS EventType,
    g.UserId,
    g.CreatedAtUtc,
    g.Payload
FROM (
    SELECT TOP (100000)
        ABS(CHECKSUM(NEWID())) % 5 AS EventTypeRoll,
        ABS(CHECKSUM(NEWID())) % 5000 + 1 AS UserId,
        DATEADD(SECOND, -(ABS(CHECKSUM(NEWID())) % (180 * 24 * 60 * 60)), SYSUTCDATETIME()) AS CreatedAtUtc,
        'Payload-' + CAST(ABS(CHECKSUM(NEWID())) % 1000000 AS NVARCHAR(10)) AS Payload
    FROM sys.all_objects AS a
    CROSS JOIN sys.all_objects AS b
) AS g;
GO
