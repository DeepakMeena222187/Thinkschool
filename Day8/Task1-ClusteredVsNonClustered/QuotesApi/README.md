# Clustered vs Non-Clustered Indexes

Same QuotesApi app as [Day7/Task3-SetOperations/QuotesApi](../../Day7/Task3-SetOperations/QuotesApi) (no feature changes) — this task adds one standalone table and a set of scripts to observe index behavior against it.

## Schema deviation

The app's `Quotes` table only has 10 seeded rows, which is too small to show any meaningful difference between a table scan and an index seek. `dbo.EventLog` (defined in [IndexDemo.sql](IndexDemo.sql)) is a separate table, seeded with ~100,000 rows, added purely for this exercise so there's enough data for `STATISTICS IO` to show a real gap. It is not wired into the EF Core app, its `DbContext`, or any migration — the same kind of documented schema deviation used for the tagging tables in the Day 7 Task 3 exercise.

## Files, in run order

1. **[IndexDemo.sql](IndexDemo.sql)** — creates `dbo.EventLog` and set-based-inserts ~100,000 rows.
2. **[BaselineQueries.sql](BaselineQueries.sql)** — runs Query A and Query B with `STATISTICS IO` before any non-clustered index exists.
3. **[CreateIndexes.sql](CreateIndexes.sql)** — creates the two non-clustered indexes.
4. **[AfterIndexQueries.sql](AfterIndexQueries.sql)** — runs the identical Query A and Query B again, so the logical-reads numbers from step 2 and step 4 are an apples-to-apples before/after comparison.
5. **[WriteCostDemo.sql](WriteCostDemo.sql)** — a single-row `INSERT` with `STATISTICS IO`, to observe the write-side cost of maintaining 3 structures instead of 1.

## A data-generation bug that's worth knowing about

The original draft of `IndexDemo.sql` fed a volatile expression directly into a simple `CASE`:

```sql
CASE ABS(CHECKSUM(NEWID())) % 5
    WHEN 0 THEN 'Login' WHEN 1 THEN 'Logout' WHEN 2 THEN 'PageView' WHEN 3 THEN 'Purchase' ELSE 'Error'
END
```

This looks like it should produce ~20% per value. It didn't — the actual run produced `Error` 41%, `Login` 20%, `Logout` 16%, `PageView` 13%, `Purchase` 10%. The cause: SQL Server evaluates a non-deterministic `CASE` input separately for *each* `WHEN` comparison instead of once, so every branch check draws its own fresh `NEWID()`. That turns the assignment into a sequence of independent 1-in-5 draws: `P(Login) = 1/5`, `P(Logout) = 4/5 × 1/5`, `P(PageView) = (4/5)² × 1/5`, `P(Purchase) = (4/5)³ × 1/5`, and `P(Error, i.e. every branch missed) = (4/5)⁴ ≈ 41%` — which matches the observed skew almost exactly. The fix (in the current `IndexDemo.sql`) computes the roll once in a derived table and has the outer `CASE` only *read* that column, giving a clean ~20% split (verified: 19816–20197 per value across 100,000 rows). The lesson generalizes: never put a non-deterministic expression directly as a simple `CASE`'s input — materialize it first.

## Index design

- **The clustered index is implicit.** `Id INT IDENTITY PRIMARY KEY` creates the clustered index automatically — there's no separate `CREATE CLUSTERED INDEX` statement because the table's data rows are already physically sorted by `Id` as a side effect of the primary key.
- **`IX_EventLog_EventType`** is a single-column non-clustered index on `EventType`, aimed at Query A's equality predicate.
- **`IX_EventLog_UserId_CreatedAtUtc`** is a composite index on `(UserId, CreatedAtUtc)`, aimed at Query B. `UserId` is listed first because it's the equality match — an index seek narrows to exactly one `UserId` value first — and `CreatedAtUtc` second because it's the range predicate; a range column has to come after the equality column(s) in a composite index for the seek to stay efficient.

## Measured results (STATISTICS IO, logical reads on `dbo.EventLog`)

| | Before indexes | After indexes |
|---|---|---|
| Query A (`EventType = 'Login'`, ~20% of rows) | 1,068 | **1,068 — unchanged** |
| Query B (`UserId = 2500 AND CreatedAtUtc > ...`, 2 rows) | 1,068 | **8** |

**Query B behaves as expected**: the composite index seek plus range scan drops logical reads from 1,068 to 8 — roughly a 130x reduction — because both predicate columns are covered by the index and the match is tiny.

**Query A does not** — and this is the more instructive result. The execution plan after indexing still shows a `Clustered Index Scan`, not an index seek:

```
|--Clustered Index Scan(OBJECT:(...PK__EventLog...), WHERE:([EventType]=N'Login'))
```

`IX_EventLog_EventType` isn't a covering index (the query is `SELECT *`), so using it to find the ~20,000 matching rows would mean one key lookup back into the clustered index per matching row. Forcing that plan with `WITH (INDEX(IX_EventLog_EventType))` and re-running Query A confirms why the optimizer avoids it: **60,759 logical reads** — about 57x worse than the 1,068-read scan. At ~20% selectivity, scanning the whole table once is cheaper than ~20,000 individual key lookups. This is the practical version of a rule of thumb: a non-covering non-clustered index only pays off once a predicate is selective enough (rough guideline: single-digit percent of rows or fewer) that seek-plus-lookup beats a scan. `IX_EventLog_EventType` isn't useless — a much rarer `EventType` value, or a query that only needed `EventType`/`Id` (a covering case), would see it used — but for `'Login'` at ~20% selectivity with `SELECT *`, it isn't.

**Write cost**: after both non-clustered indexes exist, the single-row `INSERT` in `WriteCostDemo.sql` shows **24 logical reads** for `dbo.EventLog` — the cost of locating the insertion point in all 3 structures (the clustered index plus 2 non-clustered indexes) rather than just 1. This is the standing tradeoff for every future write against this table: faster reads for selective queries, at the cost of 3x the index-maintenance work on every write.
