-- Run this after SetupDeadlockXESession.sql has been run once AND a
-- Deadlock_SessionA.sql / Deadlock_SessionB.sql concurrent run has
-- produced error 1205. Returns the most recent deadlock graph(s) first.
--
-- On-prem SQL Server can use trace flag 1222 (DBCC TRACEON(1222, -1)) to
-- write deadlock graphs to the error log, and ships a server-scoped
-- 'system_health' XEvents session that captures them automatically with
-- no setup. Neither exists on Azure SQL Database: it's a PaaS service
-- with no DBCC TRACEON, no error-log access, and — contrary to on-prem —
-- no built-in 'system_health' session either (sys.dm_xe_sessions and
-- sys.dm_xe_session_targets aren't even valid object names on Azure SQL
-- Database; querying them returns "Invalid object name", confirmed
-- against QuotesApiDay7). Extended Events on Azure SQL Database are
-- scoped per-database, and capturing deadlock graphs requires creating
-- your own database-scoped session for the sqlserver.database_xml_deadlock_report
-- event — see SetupDeadlockXESession.sql, which must be run (and left
-- running) before the deadlock occurs.

SET QUOTED_IDENTIFIER ON;
GO

;WITH ring_buffer AS
(
    SELECT CAST(t.target_data AS XML) AS rb
    FROM sys.dm_xe_database_sessions AS s
    INNER JOIN sys.dm_xe_database_session_targets AS t
        ON CAST(t.event_session_address AS BINARY(8)) = CAST(s.address AS BINARY(8))
    WHERE s.name = N'deadlocks'
      AND t.target_name = N'ring_buffer'
),
dx AS
(
    SELECT dxdr.evtdata.query('.') AS deadlock_xml_deadlock_report
    FROM ring_buffer
    CROSS APPLY rb.nodes('/RingBufferTarget/event[@name=''database_xml_deadlock_report'']') AS dxdr(evtdata)
)
SELECT
    d.value('(/event/@timestamp)[1]', 'datetime2') AS deadlock_timestamp,
    d.query('/event/data[@name=''xml_report'']/value/deadlock') AS deadlock_graph
FROM dx
CROSS APPLY deadlock_xml_deadlock_report.nodes('/event') AS ev(d)
ORDER BY deadlock_timestamp DESC;
