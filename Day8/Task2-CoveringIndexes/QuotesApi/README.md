# Covering Indexes

Same QuotesApi app as [Day8/Task1-ClusteredVsNonClustered/QuotesApi](../../Task1-ClusteredVsNonClustered/QuotesApi) (no feature changes) — this task reuses the `dbo.EventLog` table and the `IX_EventLog_UserId_CreatedAtUtc` index that Task 1 already created and seeded on the `QuotesApiDay7` Azure SQL database. Nothing here recreates or reseeds `EventLog`; it only adds one more index on top of it.

## Files, in run order

1. **[BeforePlan.sql](BeforePlan.sql)** — runs the query with only `IX_EventLog_UserId_CreatedAtUtc` in place, capturing `STATISTICS IO` and the showplan.
2. **[CreateCoveringIndex.sql](CreateCoveringIndex.sql)** — creates `IX_EventLog_UserId_Covering`.
3. **[AfterPlan.sql](AfterPlan.sql)** — runs the identical query again, so the before/after plans and logical-reads numbers are an apples-to-apples comparison.

## What a covering index is

A covering index is a non-clustered index that contains every column a query needs — both the columns in the `WHERE`/`JOIN`/`ORDER BY` and the columns in the `SELECT` list — so SQL Server can answer the query by reading the index alone, without a key lookup back into the clustered index (or heap) to fetch the remaining columns.

## Why `INCLUDE` differs from adding a column to the index key

`IX_EventLog_UserId_CreatedAtUtc` from Task 1 has `UserId` and `CreatedAtUtc` as **key** columns. Key columns:

- determine the index's sort order (the index is physically ordered by its key columns, in order),
- are used by the optimizer to seek or perform range scans,
- and count toward the index key's size limit (900 bytes / 16 key columns).

`IX_EventLog_UserId_Covering`, created in this task, keys on `UserId` alone and adds `EventType`, `CreatedAtUtc`, and `Payload` as **`INCLUDE`d** columns. Included columns:

- are stored only at the leaf level of the index, not at every level of the B-tree,
- do **not** affect the index's sort order — the index is still ordered strictly by `UserId`,
- do **not** count against the key-size limit, which lets wide or large-data-type columns (like `Payload NVARCHAR(200)`) ride along without bloating every non-leaf page,
- but still let the index satisfy the `SELECT` list directly, so a query that only needs those columns for output (not for seeking/sorting) avoids the key lookup entirely.

In short: put a column in the key only if the query needs to seek, range-scan, or sort on it. Put it in `INCLUDE` if the query only needs to read it back — that gets the covering benefit without paying the cost (larger key, more expensive index maintenance on updates to that column, lower B-tree fan-out) of making it part of the key.

## A script correction worth noting

The task originally called for `SET STATISTICS IO ON;` and `SET SHOWPLAN_TEXT ON;` in the same batch. That fails: SQL Server requires `SET SHOWPLAN_TEXT ON` to be the *only* statement in its batch (`HResult 0x42B: The SET SHOWPLAN statements must be the only statements in the batch.`). It also wouldn't have given a meaningful combination anyway — in `SHOWPLAN_TEXT` mode the query is compiled but not executed, so `STATISTICS IO`'s actual-reads counters never populate. `BeforePlan.sql` and `AfterPlan.sql` both run the query twice instead: once under `STATISTICS IO` alone (actual logical reads), then once under `SHOWPLAN_TEXT` alone (plan shape).

## Measured results (run against `QuotesApiDay7` on Azure SQL, `EventLog` at 100,003 rows)

The query used for both plans:

```sql
SELECT EventType, UserId, CreatedAtUtc, Payload
FROM dbo.EventLog
WHERE UserId = 2500;
```

### Before — only `IX_EventLog_UserId_CreatedAtUtc` in place

`STATISTICS IO`:

```
Table '[dbo].[EventLog]'. Scan count 1, logical reads 65, physical reads 0, page server reads 0, read-ahead reads 0, page server read-ahead reads 0, lob logical reads 0, lob physical reads 0, lob page server reads 0, lob read-ahead reads 0, lob page server read-ahead reads 0.
```

**Logical reads: 65**

`SHOWPLAN_TEXT`:

```
  |--Nested Loops(Inner Join, OUTER REFERENCES:([QuotesApiDay7].[dbo].[EventLog].[Id]))
       |--Index Seek(OBJECT:([QuotesApiDay7].[dbo].[EventLog].[IX_EventLog_UserId_CreatedAtUtc]), SEEK:([QuotesApiDay7].[dbo].[EventLog].[UserId]=(2500)) ORDERED FORWARD)
       |--Clustered Index Seek(OBJECT:([QuotesApiDay7].[dbo].[EventLog].[PK__EventLog__3214EC07E1A2BE9B]), SEEK:([QuotesApiDay7].[dbo].[EventLog].[Id]=[QuotesApiDay7].[dbo].[EventLog].[Id]) LOOKUP ORDERED FORWARD)
```

As expected: the optimizer seeks `IX_EventLog_UserId_CreatedAtUtc` to find the `UserId = 2500` rows, then for each one does a `Clustered Index Seek ... LOOKUP` — the textual-showplan rendering of a key lookup — back into the clustered index to fetch `EventType` and `Payload`. The `Nested Loops` operator drives one lookup per matched row.

### After — `IX_EventLog_UserId_Covering` added

`STATISTICS IO`:

```
Table '[dbo].[EventLog]'. Scan count 1, logical reads 3, physical reads 0, page server reads 0, read-ahead reads 0, page server read-ahead reads 0, lob logical reads 0, lob physical reads 0, lob page server reads 0, lob read-ahead reads 0, lob page server read-ahead reads 0.
```

**Logical reads: 3**

`SHOWPLAN_TEXT`:

```
  |--Index Seek(OBJECT:([QuotesApiDay7].[dbo].[EventLog].[IX_EventLog_UserId_Covering]), SEEK:([QuotesApiDay7].[dbo].[EventLog].[UserId]=CONVERT_IMPLICIT(int,[@1],0)) ORDERED FORWARD)
```

A single `Index Seek` on `IX_EventLog_UserId_Covering` — no `Key Lookup`, no `Nested Loops`. Every column the query needs (`EventType`, `UserId`, `CreatedAtUtc`, `Payload`) is present in the index itself (key or `INCLUDE`d), so the leaf level alone answers the query.

### Summary

| | Before (`IX_EventLog_UserId_CreatedAtUtc` only) | After (`IX_EventLog_UserId_Covering` added) |
|---|---|---|
| Plan | Index Seek + Key Lookup (Nested Loops) | Index Seek only |
| Logical reads | 65 | **3** |

Adding `EventType`, `CreatedAtUtc`, and `Payload` as `INCLUDE`d columns cut logical reads by ~95% (65 → 3, a ~22x reduction) for this query, by eliminating the clustered-index lookup that was previously needed once per matched row (21 rows matched `UserId = 2500`).
