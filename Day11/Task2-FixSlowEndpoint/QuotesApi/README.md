# Fix the Slow Endpoint (N+1 -> One Round-Trip)

Same `QuotesApi` app as [Day11/Task1-ProfileSlowEndpoint/QuotesApi](../../Task1-ProfileSlowEndpoint/QuotesApi), copied unchanged (minus `bin`/`obj`) and extended with one new endpoint. This task still reuses `dbo.EventLog` on the `QuotesApiDay7` Azure SQL database unchanged — no new table, no reseed, and (per the check below) **no new index**.

## Files

| File | What it does |
|---|---|
| [Extensions/FastEndpointExtensions.cs](Extensions/FastEndpointExtensions.cs) | New — `GET /api/users-with-events-fast`, the fixed version |
| [Extensions/SlowEndpointExtensions.cs](Extensions/SlowEndpointExtensions.cs) | Unchanged from Task1 — `GET /api/users-with-events-slow` kept as-is for comparison |
| [Program.cs](Program.cs) | One line added: `app.MapFastEndpoints();` |
| [ConfirmIndexUsage.sql](ConfirmIndexUsage.sql) | The index-usage check described below |

## The fix

`GET /api/users-with-events-fast` in [Extensions/FastEndpointExtensions.cs](Extensions/FastEndpointExtensions.cs):

1. Same first query as the slow endpoint: up to 200 distinct `UserId` values via `db.EventLogs.Select(e => e.UserId).Distinct().Take(200).ToListAsync()`.
2. **One** query for every matching row across all of those users: `db.EventLogs.Where(e => userIds.Contains(e.UserId)).Select(e => new { e.Id, e.UserId, e.EventType, e.CreatedAtUtc, e.Payload }).ToListAsync()` — a projection to an anonymous type carrying only the five columns the response needs, not full `EventLog` entities.
3. Groups the already-materialized rows by `UserId` in memory (`rows.GroupBy(r => r.UserId)` — LINQ-to-Objects over a `List<T>`, not translated to SQL; EF Core cannot translate a `GroupBy` feeding this shape of projection into a single round-trip without changing the query itself).
4. Returns the identical JSON shape as the slow endpoint: `[{ userId, eventCount, events }, ...]`.

Total database round-trips per request: **2** (the distinct-UserIds query, then the one bulk fetch) instead of the slow endpoint's up to 201.

## Confirmed: exact EF-emitted SQL for the bulk query

Ran the app locally (`dotnet run --launch-profile http`, port 5041, same setup as Task1) and called `GET /api/users-with-events-fast`. [appsettings.Development.json](appsettings.Development.json) already has `Microsoft.EntityFrameworkCore.Database.Command` at `Debug`, so the exact SQL is in the app's own console log — no extra instrumentation needed:

```
Executing DbCommand [Parameters=[@userIds1='?' (DbType = Int32), @userIds2='?' (DbType = Int32), ... @userIds200='?' (DbType = Int32)], CommandType='Text', CommandTimeout='30']
SELECT [e].[Id], [e].[UserId], [e].[EventType], [e].[CreatedAtUtc], [e].[Payload]
FROM [EventLog] AS [e]
WHERE [e].[UserId] IN (@userIds1, @userIds2, ..., @userIds200)
```

`db.EventLogs.Where(e => userIds.Contains(e.UserId))` translates to a single command with 200 individually-named parameters and a literal `IN (...)` list — not `OPENJSON`/a table-valued parameter (that translation exists in EF Core for some provider/version combinations but isn't what this app's EF Core version produced here) — and, critically, **one single `ExecuteReader` call**: one round-trip to SQL Server carries all 200 predicates, versus the slow endpoint's 200 separate `ExecuteReader` calls. Same request logged fetching all rows in a single command (130ms) for 200 distinct users:

```
[...] QuotesApi.FastEndpoint: users-with-events-fast: fetched 3993 rows for 200 distinct UserIds in one database round-trip instead of 200 separate ones
```

## Indexing check — actual execution plan, not assumed

The task calls for confirming this with `STATISTICS IO`/`STATISTICS XML`, the same method Task1 used, rather than assuming the existing `IX_EventLog_UserId_Covering` (added in Day8 Task2, still the only relevant index — see Task1's README for the full index inventory) handles this new query shape. **No index was created for this task.**

[ConfirmIndexUsage.sql](ConfirmIndexUsage.sql) reproduces the exact SQL captured above — same `SELECT` list, same table, same `WHERE [e].[UserId] IN (...)` shape — with the 200 parameter placeholders replaced by 200 real, distinct `UserId` values (`1`..`200`, fetched from the live table) so the plan reflects real data instead of an unbound query. Ran against `QuotesApiDay7` via `SET STATISTICS IO ON` and `SET STATISTICS XML ON` (checked 2026-08-21):

`STATISTICS IO`:

```
Table '[dbo].[EventLog]'. Scan count 200, logical reads 696, physical reads 0, ...
```

`STATISTICS XML` (relevant fragment):

```xml
<RelOp PhysicalOp="Nested Loops" LogicalOp="Inner Join">
  <RelOp PhysicalOp="Constant Scan" LogicalOp="Constant Scan" ... />
  <RelOp PhysicalOp="Index Seek" LogicalOp="Index Seek" ActualRows="3993" ActualExecutions="200" ActualLogicalReads="633">
    <Object Table="[EventLog]" Index="[IX_EventLog_UserId_Covering]" IndexKind="NonClustered" />
  </RelOp>
</RelOp>
```

**Confirmed, not assumed:** the optimizer turns the 200-value `IN (...)` list into a `Constant Scan` (one row per literal value) driving a `Nested Loops` into 200 `Index Seek`s against `IX_EventLog_UserId_Covering` — the same covering index, same no-`Key-Lookup` seek Task1 confirmed for the single-user case (3 logical reads per value there; 696 / 200 ≈ 3.48 here, consistent). Every column the query needs (`Id`, `UserId`, `EventType`, `CreatedAtUtc`, `Payload`) is still satisfied by the index alone.

This is expected and is not a problem: SQL Server choosing to internally seek once per value for a 200-value `IN` list is a normal, cheap plan shape (200 index seeks server-side, ~3 logical reads each) — a world apart from 200 *client round-trips*. The fix in this task was never about making each per-user lookup cheaper (Task1 already showed it's about as cheap as a single-table query gets); it was about collapsing 200 sequential HTTP-request-to-SQL-Server round-trips into one. `IX_EventLog_UserId_Covering` already serves that one bulk query exactly as well as it served 200 individual ones, so nothing needed to change at the index level — consistent with Task1's conclusion that this was a round-trip-count problem, not a missing-index problem.

## Measured: single-request comparison

Both endpoints hit locally against the same live data (`dotnet run`, port 5041):

| Endpoint | Round-trips | Wall time (single unloaded request) |
|---|---|---|
| `GET /api/users-with-events-slow` (Task1) | up to 201 | ~8.7s |
| `GET /api/users-with-events-fast` (this task) | 2 | ~0.56s |

Same 200 distinct users, same ~3,993 total matching rows, same JSON shape out — about 15x faster on a single unloaded request, and (per Task1's load test) the slow endpoint's real failure mode was concurrent-request collapse under connection-pool contention, which a 2-round-trip endpoint does not suffer from in the same way: there's no long chain of sequential awaited round-trips per request left to contend over the pool.

## What I learned

The obvious "faster" fix here isn't a better index — Task1 already confirmed the per-user query was already a cheap, fully-covered index seek. The fix is entirely about collapsing the round-trip count: one query for the id list, one query for every row across all ids, group in memory. Checking the actual plan for the new query shape (rather than assuming the old covering index would "just work" for a 200-value `IN` list) showed the optimizer handles it exactly the way it handled the single-value case — 200 cheap index seeks, still no key lookups, all within the *same* single database round-trip. The index didn't need to change; only the C# did.
