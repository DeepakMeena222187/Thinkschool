-- Run this once, before the Deadlock_SessionA/B run, and before
-- CaptureDeadlockGraph.sql can return anything.
--
-- Unlike on-prem SQL Server, Azure SQL Database does NOT ship a
-- server-scoped 'system_health' Extended Events session running by
-- default — sys.dm_xe_sessions / sys.dm_xe_session_targets aren't even
-- valid object names here (Azure SQL Database is PaaS; there's no
-- server-level XEvents surface, and no DBCC TRACEON(1222) / error log
-- access either). Extended Events on Azure SQL Database are scoped to the
-- database instead, and you have to create the deadlock-capturing session
-- yourself. This is the one-time setup for that.
--
-- Safe to leave running; DROP EVENT SESSION [deadlocks] ON DATABASE
-- afterward if you want to remove it.

IF EXISTS (SELECT 1 FROM sys.database_event_sessions WHERE name = 'deadlocks')
BEGIN
    ALTER EVENT SESSION [deadlocks] ON DATABASE STATE = STOP;
    DROP EVENT SESSION [deadlocks] ON DATABASE;
END
GO

CREATE EVENT SESSION [deadlocks] ON DATABASE
ADD EVENT sqlserver.database_xml_deadlock_report
ADD TARGET package0.ring_buffer
WITH
(
    STARTUP_STATE = ON,
    MAX_MEMORY = 4 MB
);
GO

ALTER EVENT SESSION [deadlocks] ON DATABASE
STATE = START;
GO
