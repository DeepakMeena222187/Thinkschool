-- Run this first, once, before the demo pairs below. It doesn't touch
-- dbo.EventLog — it only reports how READ COMMITTED behaves on this
-- database, which matters for interpreting (not for reproducing) the
-- NonRepeatableRead_SessionA/B.sql and PhantomRead_SessionA/B.sql results.
--
-- is_read_committed_snapshot_on = 1 (common default on Azure SQL Database):
--   READ COMMITTED is statement-level snapshot-based. Readers don't block
--   on writers and don't dirty-read either; each statement sees whatever
--   was committed as of that statement's start.
-- is_read_committed_snapshot_on = 0:
--   READ COMMITTED is lock-based. Readers take shared locks released right
--   after each statement, so they can briefly block behind a writer's
--   exclusive lock, but never see uncommitted data.
--
-- Either setting still produces the non-repeatable-read and phantom-read
-- anomalies in this demo (both allow a later statement in the same
-- transaction to see another transaction's commit that landed in between).
-- REPEATABLE READ and SERIALIZABLE are unaffected by this setting either
-- way — both always use locking, never versioning.
SELECT name, is_read_committed_snapshot_on
FROM sys.databases
WHERE name = 'QuotesApiDay7';
