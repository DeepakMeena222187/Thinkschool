-- Phantom read demo — Session B (the writer), prevented variant.
-- Identical to PhantomRead_SessionB.sql. Under SERIALIZABLE on Session A's
-- side, this INSERT will block on Session A's range lock covering
-- UserId = 9999 until Session A's COMMIT TRAN runs, instead of committing
-- immediately.
-- Run concurrently with PhantomRead_Prevented_SessionA.sql (not
-- sequentially).

WAITFOR DELAY '00:00:02';

INSERT INTO dbo.EventLog (EventType, UserId, CreatedAtUtc, Payload)
VALUES ('Login', 9999, SYSUTCDATETIME(), 'phantom-row'); -- blocks until Session A commits
