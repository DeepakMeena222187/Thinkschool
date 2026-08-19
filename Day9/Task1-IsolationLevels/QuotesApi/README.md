# Isolation Levels

Same `QuotesApi` app as [Day8/Task2-CoveringIndexes/QuotesApi](../../../Day8/Task2-CoveringIndexes/QuotesApi) (no feature changes) â€” this task reuses `dbo.EventLog` on the `QuotesApiDay7` Azure SQL database, already created and seeded with ~100,003 rows by [Day8/Task1-ClusteredVsNonClustered](../../../Day8/Task1-ClusteredVsNonClustered/QuotesApi/IndexDemo.sql). Nothing here recreates or reseeds `EventLog`.

The goal is to force three classic isolation-level anomalies â€” dirty read, non-repeatable read, phantom read â€” by making two SQL sessions interleave deterministically with `WAITFOR DELAY`, then show that raising the reader's isolation level to the appropriate level prevents each anomaly.

## Before running anything: check RCSI status

Run [CheckRcsiStatus.sql](CheckRcsiStatus.sql) once against `QuotesApiDay7` first (read-only, doesn't touch `EventLog`). Azure SQL Database commonly has `READ_COMMITTED_SNAPSHOT` (RCSI) on by default, which changes *how* READ COMMITTED behaves without changing what these demos show:

> **Measured on 2026-08-19**: `name                                                                                                                             is_read_committed_snapshot_on -------------------------------------------------------------------------------------------------------------------------------- ----------------------------- QuotesApiDay7                                                                                                                                                1  (1 rows affected)`

- **RCSI on**: READ COMMITTED is statement-level snapshot-based â€” a reader never blocks on a writer and never dirty-reads either; each statement just sees whatever was committed as of that statement's start.
- **RCSI off**: READ COMMITTED is lock-based â€” a reader takes a shared lock released immediately after each statement, so it can briefly block behind a writer's exclusive lock, but still never sees uncommitted data.

Either way, the non-repeatable-read and phantom-read *anomaly* pairs (both using READ COMMITTED) still reproduce â€” a later statement in the same transaction can see another transaction's commit that landed in between, regardless of which mechanism is in play. The two *prevented* pairs (REPEATABLE READ, SERIALIZABLE) are unaffected by RCSI either way â€” both always take locks, never rely on versioning.

## Files

Each pair below must run as **two separate, concurrent `sqlcmd` processes** â€” Session A and Session B started at (approximately) the same time, not one after the other. The `WAITFOR DELAY` calls inside the scripts are what force the deterministic interleaving; if you run them sequentially instead of concurrently, the anomaly (or its prevention) won't reproduce, because Session B's write would already be committed and finished before Session A ever starts.

| Pair | Anomaly / prevention | Session A isolation level |
|---|---|---|
| [DirtyRead_SessionA.sql](DirtyRead_SessionA.sql) / [DirtyRead_SessionB.sql](DirtyRead_SessionB.sql) | Dirty read (anomaly) | READ UNCOMMITTED |
| [NonRepeatableRead_SessionA.sql](NonRepeatableRead_SessionA.sql) / [NonRepeatableRead_SessionB.sql](NonRepeatableRead_SessionB.sql) | Non-repeatable read (anomaly) | READ COMMITTED |
| [PhantomRead_SessionA.sql](PhantomRead_SessionA.sql) / [PhantomRead_SessionB.sql](PhantomRead_SessionB.sql) | Phantom read (anomaly) | READ COMMITTED |
| [NonRepeatableRead_Prevented_SessionA.sql](NonRepeatableRead_Prevented_SessionA.sql) / [NonRepeatableRead_Prevented_SessionB.sql](NonRepeatableRead_Prevented_SessionB.sql) | Non-repeatable read (prevented) | REPEATABLE READ |
| [PhantomRead_Prevented_SessionA.sql](PhantomRead_Prevented_SessionA.sql) / [PhantomRead_Prevented_SessionB.sql](PhantomRead_Prevented_SessionB.sql) | Phantom read (prevented) | SERIALIZABLE |

**Dirty read's own prevention needs no separate script pair.** `DirtyRead_SessionB.sql` is unchanged; the only thing that changes is Session A's isolation level. Re-run `DirtyRead_SessionA.sql` with `SET TRANSACTION ISOLATION LEVEL READ COMMITTED;` (SQL Server's default, and already the level everywhere else in this set) in place of `READ UNCOMMITTED`, and the dirty read disappears: `READ COMMITTED` cannot see Session B's uncommitted `UPDATE` because Session B still holds an exclusive lock on that row until it commits or rolls back â€” Session A's `SELECT` just waits (or, under Read Committed Snapshot Isolation, reads the last *committed* value) instead of reading the in-flight one.

## The isolation-level-to-anomaly table

| Anomaly | What it means | Isolation level that prevents it |
|---|---|---|
| Dirty read | A transaction reads a row that another transaction has changed but not yet committed â€” the value can vanish on rollback | READ COMMITTED |
| Non-repeatable read | A transaction re-reads the same row twice and gets two different values, because another transaction committed a change to it in between | REPEATABLE READ |
| Phantom read | A transaction re-runs the same range/set predicate twice and gets a different set of rows, because another transaction committed an insert or delete matching that predicate in between | SERIALIZABLE |

Each level in the right column prevents its own anomaly and every anomaly to its left, at the cost of holding locks longer (or, for SERIALIZABLE, range/key-range locks) and reducing concurrency.

## Running a pair concurrently

Replace `<server>`, `<database>` (`QuotesApiDay7`), and the login placeholders with the real values â€” do not commit real credentials into this repo.

### PowerShell

```powershell
$server = "<server>.database.windows.net"
$database = "QuotesApiDay7"
$user = "<user>"
$password = "<password>"

$jobA = Start-Job -ScriptBlock {
    sqlcmd -S $using:server -d $using:database -U $using:user -P $using:password -i "DirtyRead_SessionA.sql"
}
$jobB = Start-Job -ScriptBlock {
    sqlcmd -S $using:server -d $using:database -U $using:user -P $using:password -i "DirtyRead_SessionB.sql"
}

Wait-Job $jobA, $jobB
Receive-Job $jobA
Receive-Job $jobB
Remove-Job $jobA, $jobB
```

Swap the two `-i` filenames to run the `NonRepeatableRead_*`, `PhantomRead_*`, `NonRepeatableRead_Prevented_*`, or `PhantomRead_Prevented_*` pairs.

### Bash (if running from a POSIX shell instead)

```bash
server="<server>.database.windows.net"
database="QuotesApiDay7"
user="<user>"
password="<password>"

sqlcmd -S "$server" -d "$database" -U "$user" -P "$password" -i DirtyRead_SessionA.sql > sessionA.out &
sqlcmd -S "$server" -d "$database" -U "$user" -P "$password" -i DirtyRead_SessionB.sql > sessionB.out &
wait
cat sessionA.out sessionB.out
```

The `&` backgrounds each `sqlcmd` call and `wait` blocks until both finish â€” this is what makes the two sessions genuinely overlap instead of running one after the other.

### A note on Azure SQL Basic tier and concurrency

`QuotesApiDay7` is expected to be a low-DTU tier. Two concurrent `sqlcmd` connections should be well within its connection/worker limits, but a low-DTU database under load can introduce enough scheduling latency to shift the `WAITFOR` timings. If a run's output looks inconsistent with the expected anomaly/prevention â€” e.g., the non-repeatable-read anomaly pair shows the *same* value on both selects, or a prevented pair's blocking doesn't happen â€” treat that as a signal to inspect (check `sys.dm_exec_requests` / `sys.dm_tran_locks` for unexpected waits, or re-check elapsed time per session) rather than just re-running until it looks right.

### A note on the non-repeatable-read and phantom-read pairs

Both `Id = 2` (non-repeatable read) and `UserId = 9999` (phantom read) are shared across the anomaly and prevented variants of each demo:

- `NonRepeatableRead_SessionB.sql` and `NonRepeatableRead_Prevented_SessionB.sql` both set `Payload = 'CHANGED-BY-B'` on `Id = 2`. If you run the anomaly variant first, the prevented variant's two `SELECT`s will both show `'CHANGED-BY-B'` (same value going in and out) rather than a value actually changing â€” that's expected, and the real evidence of prevention is that Session B's `UPDATE` **doesn't return until Session A's `COMMIT TRAN` runs** (i.e., Session B's script takes noticeably longer to finish than its own 2-second `WAITFOR`), not the Payload value itself.
- `PhantomRead_SessionB.sql` and `PhantomRead_Prevented_SessionB.sql` both `INSERT` a new `UserId = 9999` row every time they run, so the baseline `COUNT(*)` for `UserId = 9999` will climb by one with each run across the whole set. The evidence that matters is still the **delta between Session A's two `COUNT(*)` calls within a single run** (0 under READ COMMITTED's anomaly, unchanged under SERIALIZABLE's prevention) and, for the prevented variant, that Session B's `INSERT` blocks until Session A commits.

## Captured output

<!-- TODO: paste the actual sqlcmd output from each concurrent run below, replacing the placeholders. Include both sessions' output and, ideally, a timestamp or elapsed-time note showing they overlapped (e.g., Session B's script finishing noticeably later than its own WAITFOR when blocked). -->

### Dirty read (anomaly) â€” READ UNCOMMITTED

```
Id          Payload
----------- --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
          1 DIRTY-UNCOMMITTED

(1 rows affected)
(elapsed: 3s)
```

```

(1 rows affected)
Id          Payload
----------- --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
          1 Payload-730997

(1 rows affected)
(elapsed: 5.82s)
```

### Non-repeatable read (anomaly) â€” READ COMMITTED

```
Id          Payload
----------- --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
          2 Payload-21844

(1 rows affected)
Id          Payload
----------- --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
          2 CHANGED-BY-B

(1 rows affected)
(elapsed: 5.58s)
```

```

(1 rows affected)
(elapsed: 2.54s)
```

### Non-repeatable read (prevented) â€” REPEATABLE READ

```
Id          Payload
----------- --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
          2 CHANGED-BY-B

(1 rows affected)
Id          Payload
----------- --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
          2 CHANGED-BY-B

(1 rows affected)
(elapsed: 5.62s)
```

```

(1 rows affected)
(elapsed: 5.52s - its own WAITFOR is only 2s, so anything meaningfully longer confirms it blocked on Session A's lock)
```

### Phantom read (anomaly) â€” READ COMMITTED

```

-----------
          0

(1 rows affected)

-----------
          1

(1 rows affected)
(elapsed: 5.6s)
```

```

(1 rows affected)
(elapsed: 2.69s)
```

### Phantom read (prevented) â€” SERIALIZABLE

```

-----------
          1

(1 rows affected)

-----------
          1

(1 rows affected)
(elapsed: 6.02s)
```

```

(1 rows affected)
(elapsed: 5.96s - its own WAITFOR is only 2s, so anything meaningfully longer confirms it blocked on Session A's lock)
```
