CREATE NONCLUSTERED INDEX IX_EventLog_UserId_Covering
ON dbo.EventLog(UserId)
INCLUDE (EventType, CreatedAtUtc, Payload);
