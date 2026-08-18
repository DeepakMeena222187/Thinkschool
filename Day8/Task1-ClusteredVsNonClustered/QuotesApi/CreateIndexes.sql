CREATE NONCLUSTERED INDEX IX_EventLog_EventType ON dbo.EventLog(EventType);
CREATE NONCLUSTERED INDEX IX_EventLog_UserId_CreatedAtUtc ON
    dbo.EventLog(UserId, CreatedAtUtc);
