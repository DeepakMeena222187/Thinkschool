# Deadlock

Same `QuotesApi` app as [Day9/Task1-IsolationLevels/QuotesApi](../../Task1-IsolationLevels/QuotesApi) (no feature changes) — this task reuses `dbo.EventLog` on the `QuotesApiDay7` Azure SQL database, already created and seeded. Nothing here recreates or reseeds `EventLog`.

The goal is to force a genuine deadlock — two sessions each holding a lock the other one needs — by making two SQL sessions lock the same two rows in opposite orders, then show that a simple fix (always lock rows in the same order) removes the deadlock entirely.

## Files

| Pair | What it shows |
|---|---|
| [Deadlock_SessionA.sql](Deadlock_SessionA.sql) / [Deadlock_SessionB.sql](Deadlock_SessionB.sql) | Deadlock (anomaly) — one session is killed with error 1205 |
| [Fixed_SessionA.sql](Fixed_SessionA.sql) / [Fixed_SessionB.sql](Fixed_SessionB.sql) | Deadlock (fixed) — consistent lock ordering, no error, one session just waits longer |
| [SetupDeadlockXESession.sql](SetupDeadlockXESession.sql) | One-time setup: creates and starts the database-scoped XEvents session that captures deadlock graphs |
| [CaptureDeadlockGraph.sql](CaptureDeadlockGraph.sql) | Pulls the XML deadlock graph for the deadlock run out of that session |

Each pair must run as **two separate, concurrent `sqlcmd` processes** — Session A and Session B started at (approximately) the same time, not one after the other, same pattern as [Task1](../../Task1-IsolationLevels/QuotesApi/README.md). The `WAITFOR DELAY` calls inside the scripts are what force the deterministic interleaving.

### Why the deadlock scripts deadlock

- Session A: locks `Id = 1`, waits 3s, then locks `Id = 2`.
- Session B: waits 1s (so A's first `UPDATE` lands first), then locks `Id = 2`, waits 3s, then locks `Id = 1`.

By the time each session's `WAITFOR` finishes, the other session already holds the row it's about to ask for — A holds `Id = 1` and wants `Id = 2`; B holds `Id = 2` and wants `Id = 1`. That's a circular wait, which SQL Server's lock monitor detects and resolves by killing one session's transaction (rolled back automatically) with error 1205: `Transaction (Process ID nn) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.` The surviving session's second `UPDATE` then goes through and it commits normally.

### Why the fixed scripts don't deadlock

`Fixed_SessionA.sql` and `Fixed_SessionB.sql` are identical except both always lock `Id = 1` before `Id = 2`. Whichever session gets there first (Session A, since it starts immediately) holds `Id = 1` first; Session B's attempt to lock `Id = 1` simply blocks until Session A commits and releases it. No session is ever holding what the other one is stuck waiting to acquire in the opposite order — a queue, not a cycle. There's nothing to detect and nothing to kill. Instead of error 1205, you'll see Session B's elapsed time stretch out well past its own `WAITFOR` total (proof it blocked and waited, same evidence-by-elapsed-time approach Task 1 used for blocking).

## Running a pair concurrently

Replace `<server>`, `<database>` (`QuotesApiDay7`), and the login placeholders with the real values — do not commit real credentials into this repo.

### PowerShell

```powershell
$server = "<server>.database.windows.net"
$database = "QuotesApiDay7"
$user = "<user>"
$password = "<password>"

$jobA = Start-Job -ScriptBlock {
    sqlcmd -S $using:server -d $using:database -U $using:user -P $using:password -i "Deadlock_SessionA.sql"
}
$jobB = Start-Job -ScriptBlock {
    sqlcmd -S $using:server -d $using:database -U $using:user -P $using:password -i "Deadlock_SessionB.sql"
}

Wait-Job $jobA, $jobB
Receive-Job $jobA
Receive-Job $jobB
Remove-Job $jobA, $jobB
```

Swap the two `-i` filenames to `Fixed_SessionA.sql` / `Fixed_SessionB.sql` for the fixed run.

### Bash (if running from a POSIX shell instead)

```bash
server="<server>.database.windows.net"
database="QuotesApiDay7"
user="<user>"
password="<password>"

sqlcmd -S "$server" -d "$database" -U "$user" -P "$password" -i Deadlock_SessionA.sql > sessionA.out &
sqlcmd -S "$server" -d "$database" -U "$user" -P "$password" -i Deadlock_SessionB.sql > sessionB.out &
wait
cat sessionA.out sessionB.out
```

The `&` backgrounds each `sqlcmd` call and `wait` blocks until both finish — this is what makes the two sessions genuinely overlap instead of running one after the other.

### Expected result of the deadlock run

**One of the two `sqlcmd` processes will fail with error 1205** — this is expected and is the entire point of the exercise, not a bug to chase down. Its output will look something like:

```
Msg 1205, Level 13, State 56, Server ..., Line ...
Transaction (Process ID nn) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.
```

The other session's script completes normally (both its `UPDATE`s go through and it commits). Which of A or B is picked as the victim is not guaranteed — SQL Server's deadlock monitor picks based on factors like accumulated cost/rollback cheapness, not a fixed rule of "whoever asked second."

### Capturing the deadlock graph

**Run [SetupDeadlockXESession.sql](SetupDeadlockXESession.sql) once, before the deadlock run** — it creates and starts a database-scoped XEvents session named `deadlocks` that captures the `sqlserver.database_xml_deadlock_report` event to a ring buffer. It only needs to be run once; it stays started (`STARTUP_STATE = ON`) across reconnects and database restarts.

Trace flag 1222 (`DBCC TRACEON(1222, -1)`, which writes deadlock graphs to the SQL Server error log) does **not** work on Azure SQL Database — it's a PaaS service with no `DBCC TRACEON` and no error-log access available to a regular login.

**Correction from the original assumption in this task:** unlike on-prem SQL Server, Azure SQL Database also does **not** ship a built-in `system_health` Extended Events session running by default — `sys.dm_xe_sessions` and `sys.dm_xe_session_targets` (the server-scoped catalog views `system_health` normally lives in) aren't even valid object names there; querying them returns `Msg 208: Invalid object name`, confirmed against `QuotesApiDay7`. Extended Events on Azure SQL Database are scoped per-database instead (`sys.dm_xe_database_sessions` / `sys.dm_xe_database_session_targets`), and capturing deadlock graphs requires creating your own database-scoped session for `sqlserver.database_xml_deadlock_report` — which is what `SetupDeadlockXESession.sql` does. This only matters for *setup*; nothing about the deadlock or the fix itself changes.

After `SetupDeadlockXESession.sql` has run and a deadlock run has produced error 1205, run [CaptureDeadlockGraph.sql](CaptureDeadlockGraph.sql) against the same database to pull the deadlock graph XML back out, most recent first.

### A note on Azure SQL Basic tier and concurrency

`QuotesApiDay7` is expected to be a low-DTU tier. Two concurrent `sqlcmd` connections should be well within its connection/worker limits, but a low-DTU database under load can introduce enough scheduling latency to shift the `WAITFOR` timings. If the deadlock run doesn't produce error 1205 on either session, treat that as a signal to check timings/locks (`sys.dm_exec_requests`, `sys.dm_tran_locks`) rather than just re-running until it looks right — though in practice a genuine circular wait like this one is deterministic enough that it should reproduce on the first try.

## Captured output

**Measured on 2026-08-19** against `QuotesApiDay7`, using two concurrent background `sqlcmd` processes (via `Start-Job`/`Wait-Job`, connection details pulled from user-secrets — not typed on the command line or shown in output).

An earlier run (before `SetupDeadlockXESession.sql` existed) also deadlocked and killed **Session A** instead — proving the victim choice really isn't fixed to "whoever asked second." The run below is the one paired with a captured deadlock graph, where **Session B** was the victim.

### Deadlock (anomaly) — Session A (survivor)

```
(1 rows affected)

(1 rows affected)
(elapsed: 5.24s)
```

### Deadlock (anomaly) — Session B (victim)

```
(1 rows affected)
Msg 1205, Level 13, State 72, Server quotesapi-day7-sql, Line 20
Transaction (Process ID 92) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.
(elapsed: 5.16s)
```

Session A's second `UPDATE` (on `Id = 2`) went through right after Session B was killed and released its lock, which is why Session A's script still completes normally end-to-end.

### Deadlock graph XML

Returned by `CaptureDeadlockGraph.sql` for the run above — `victim-list` names the process that was Session B (`spid="92"`), and the two `inputbuf` blocks show each session's exact script text, confirming which is which:

```xml
<deadlock><victim-list><victimProcess id="process16995712088"/></victim-list><process-list><process id="process16995712088" taskpriority="0" logused="288" waitresource="XACT: 5:6081:0 KEY: 5:72057594048610304 (8194443284a0)" waittime="763" ownerId="1898410" transactionname="user_transaction" lasttranstarted="2026-08-19T06:20:55.767" lockMode="S" schedulerid="2" status="suspended" spid="92" ecid="0" priority="0" trancount="2" lastbatchstarted="2026-08-19T06:20:54.753" lastbatchcompleted="2026-08-19T06:20:54.720" clientapp="SQLCMD" hostname="BABYKRISH" loginname="sqladmin" isolationlevel="read committed (2)" currentdb="5" currentdbname="QuotesApiDay7"><executionStack><frame procname="unknown" line="20" queryhash="0x700da4fe47e4c845"></frame></executionStack><inputbuf>
-- Deadlock demo — Session B.
-- Starts 1 second after Session A so Session A's first UPDATE (on Id = 1)
-- lands first. This session then locks Id = 2, waits 3 seconds, and
-- reaches for Id = 1 — which Session A is holding, while Session A is
-- simultaneously reaching for the row this session holds. That circular
-- wait is exactly what forces SQL Server's deadlock monitor to pick a
-- victim and kill one session with error 1205.
-- Run concurrently with Deadlock_SessionA.sql (not sequentially).

WAITFOR DELAY '00:00:01';

BEGIN TRAN;

UPDATE dbo.EventLog SET Payload = 'LockedByB-Row2' WHERE Id = 2;

WAITFOR DELAY '00:00:03';

-- Session A holds an exclusive lock on Id = 1 by now — this blocks until
-- either A commits/rolls back, or the deadlock monitor kills one of us.
UPDATE dbo.EventLog SET Payload = 'LockedByB-Row1' WHERE Id = 1;

COMMIT TRAN;
</inputbuf></process><process id="process1699e2ed828" taskpriority="0" logused="588" waitresource="XACT: 5:6082:0 KEY: 5:72057594048610304 (61a06abd401c)" waittime="1837" ownerId="1898404" transactionname="user_transaction" lasttranstarted="2026-08-19T06:20:54.707" lockMode="S" schedulerid="2" status="suspended" spid="91" ecid="0" priority="0" trancount="2" lastbatchstarted="2026-08-19T06:20:54.707" lastbatchcompleted="2026-08-19T06:20:54.670" clientapp="SQLCMD" hostname="BABYKRISH" loginname="sqladmin" isolationlevel="read committed (2)" currentdb="5" currentdbname="QuotesApiDay7"><executionStack><frame procname="unknown" line="17" queryhash="0x700da4fe47e4c845"></frame></executionStack><inputbuf>
-- Deadlock demo — Session A.
-- Locks Id = 1 first, waits 3 seconds (long enough for Session B to grab
-- Id = 2 in the meantime), then reaches for Id = 2 — which Session B is
-- now holding. Session B, symmetrically, reaches for Id = 1 — which this
-- session is holding. Neither can proceed: SQL Server's deadlock monitor
-- detects the cycle and kills one session with error 1205.
-- Run concurrently with Deadlock_SessionB.sql (not sequentially).

BEGIN TRAN;

UPDATE dbo.EventLog SET Payload = 'LockedByA-Row1' WHERE Id = 1;

WAITFOR DELAY '00:00:03';

-- Session B holds an exclusive lock on Id = 2 by now — this blocks until
-- either B commits/rolls back, or the deadlock monitor kills one of us.
UPDATE dbo.EventLog SET Payload = 'LockedByA-Row2' WHERE Id = 2;

COMMIT TRAN;
</inputbuf></process></process-list><resource-list><xactlock xdesIdLow="6081" xdesIdHigh="0" dbid="5" id="lock16984c48280" mode="X"><UnderlyingResource><keylock hobtid="72057594048610304" dbid="5" objectname="54863c31-645f-4167-9fa9-216324a80745.dbo.EventLog" indexname="PK__EventLog__3214EC07E1A2BE9B"/></UnderlyingResource><owner-list><owner id="process1699e2ed828" mode="X"/></owner-list><waiter-list><waiter id="process16995712088" mode="S" requestType="wait"/></waiter-list></xactlock><xactlock xdesIdLow="6082" xdesIdHigh="0" dbid="5" id="lock16984c44880" mode="X"><UnderlyingResource><keylock hobtid="72057594048610304" dbid="5" objectname="54863c31-645f-4167-9fa9-216324a80745.dbo.EventLog" indexname="PK__EventLog__3214EC07E1A2BE9B"/></UnderlyingResource><owner-list><owner id="process16995712088" mode="X"/></owner-list><waiter-list><waiter id="process1699e2ed828" mode="S" requestType="wait"/></waiter-list></xactlock></resource-list></deadlock>
```

(The raw XML returned by SQL Server also includes a native `stackFrames` block per process — engine call-stack addresses used for Microsoft's own diagnostics. Trimmed here since it's not relevant to the demo; nothing else was altered.)

Reading the graph: both `xactlock` entries are `X` (exclusive) mode on the same index (`PK__EventLog...`) — one on the row Session B (`process...5712088`) owns and Session A (`process...e2ed828`) is waiting (`S`) on, and the other on the row Session A owns and Session B is waiting on. That's the circular wait made explicit — exactly what the `victim-list` resolves by killing Session B.

### Fixed (no deadlock) — Session A

```
(1 rows affected)

(1 rows affected)
(elapsed: 3.44s)
```

### Fixed (no deadlock) — Session B

```
(1 rows affected)

(1 rows affected)
(elapsed: 6.39s)
```

No error 1205 on either side. Session A's own script takes ~3.4s (one 3s `WAITFOR` plus overhead). Session B's own script, with no blocking, would take ~4s (a 1s `WAITFOR` plus a 3s `WAITFOR`) — instead it took **6.39s**, roughly 2.4s longer, which is Session B sitting blocked on Session A's lock on `Id = 1` until Session A committed, then proceeding with its own remaining `WAITFOR` and second `UPDATE`. Same evidence-by-elapsed-time signature as Task 1's blocking demos, and no deadlock victim this time.
