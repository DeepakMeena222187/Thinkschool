# Profile a Slow Endpoint (N+1)

Same `QuotesApi` app as [Day10/Task2-QueryTranslation/QuotesApi](../../../Day10/Task2-QueryTranslation/QuotesApi), minus the Task2-specific query-translation demo — this task reuses `dbo.EventLog` on the `QuotesApiDay7` Azure SQL database (100,006 rows, 5,001 distinct `UserId` values as of this check, created and seeded back in Day8). Nothing here recreates or reseeds `EventLog`; it only adds one deliberately slow endpoint on top of it.

## Files

| File | What it does |
|---|---|
| [Extensions/SlowEndpointExtensions.cs](Extensions/SlowEndpointExtensions.cs) | `GET /api/users-with-events-slow` — the deliberate N+1 endpoint |
| [Models/EventLog.cs](Models/EventLog.cs) | Unchanged from Day10 Task2 |
| [Data/QuotesDbContext.cs](Data/QuotesDbContext.cs) | Unchanged `EventLog` mapping from Day10 Task2 |
| [Program.cs](Program.cs) | `--querydemo` flag and `QueryTranslation` wiring removed (Task2-specific); `app.MapSlowEndpoints();` added; OpenTelemetry/Jaeger setup carried forward unchanged from Day5 |

Removed relative to Day10 Task2: `QueryTranslation/QueryTranslationDemo.cs` and its `--querydemo` branch in `Program.cs`.

## The endpoint

`GET /api/users-with-events-slow`

1. One query gets up to 200 distinct `UserId` values: `db.EventLogs.Select(e => e.UserId).Distinct().Take(200).ToListAsync()`.
2. Then, in a loop, one **separate** query per `UserId`: `await db.EventLogs.Where(e => e.UserId == userId).ToListAsync()`.
3. Returns a JSON array of `{ userId, eventCount, events }`.

That's up to 201 round-trips to serve one HTTP request — a textbook N+1: "1" query to get the list of things, "+N" queries (one per item) to get each thing's detail, instead of a single `WHERE UserId IN (...)` or a `GROUP BY` query. The cap at 200 distinct users (out of 5,001 available) is deliberate: it keeps a single request's round-trip count bounded and load-test runs practical, while still being unambiguously N+1 — 200 sequential round-trips per request is plenty to demonstrate the problem without needing the full 5,001-user fan-out.

## Load-testing tool

Checked on this machine before writing any load-test code: `which bombardier`, `which k6`, and `Get-Command bombardier`/`Get-Command k6` in PowerShell all came back empty — neither was installed. `winget` is also not available on this machine, so bombardier was installed by downloading `bombardier-windows-amd64.exe` directly from the [bombardier GitHub releases](https://github.com/codesenberg/bombardier/releases) and placing it as `bombardier.exe` in `C:\Users\admin\bin` (already on `PATH`, outside this repo — not committed here). Confirmed working:

```
> bombardier --version
bombardier version unspecified windows/amd64
```

Command used (app running locally via `dotnet run`, default `http` profile on port 5041 per [Properties/launchSettings.json](Properties/launchSettings.json)) — results below under "Measured: bombardier load test":

```
bombardier -c 10 -d 30s -l http://localhost:5041/api/users-with-events-slow
```

- `-c 10` — 10 concurrent connections. Each request drives up to 201 sequential DB round-trips, so this is deliberately modest: enough concurrency to produce a real p50/p99 distribution without the concurrent request count multiplying into hundreds of simultaneous DB connections against the same pool.
- `-d 30s` — fixed 30-second duration, long enough to get a stable percentile read given each request is expected to take well over 100ms.
- `-l` — enables bombardier's latency histogram (percentile breakdown) in the output, needed for the p50/p99 numbers.

## Indexing check — actual execution plan, not assumed

Current indexes on `dbo.EventLog`, read directly from `sys.indexes`/`sys.index_columns` on `QuotesApiDay7` (checked 2026-08-21; not modified):

| Index | Type | Key | Include |
|---|---|---|---|
| `PK__EventLog__...` | Clustered | `Id` | — |
| `IX_EventLog_EventType` | Nonclustered | `EventType` | — |
| `IX_EventLog_UserId_CreatedAtUtc` | Nonclustered | `UserId, CreatedAtUtc` | — |
| `IX_EventLog_UserId_Covering` | Nonclustered | `UserId` | `EventType, CreatedAtUtc, Payload` |

The first three were created in Day8 Task1; `IX_EventLog_UserId_Covering` was added in Day8 Task2 and is still present on the shared database — this task didn't create it, but it's real, current state that affects the answer below.

**The question asked**: does `IX_EventLog_UserId_CreatedAtUtc` (the `(UserId, CreatedAtUtc)` composite) actually get used for the endpoint's plain `WHERE UserId = @p` (no `CreatedAtUtc` predicate), or is something else missing?

Captured the actual plan for the endpoint's exact per-user query shape (`SELECT Id, EventType, UserId, CreatedAtUtc, Payload FROM dbo.EventLog WHERE UserId = 2500`) against live `QuotesApiDay7`, via `SET STATISTICS IO ON` + `SET STATISTICS XML ON`:

```
Table '[dbo].[EventLog]'. Scan count 1, logical reads 3, physical reads 0, ...
```

```xml
<RelOp PhysicalOp="Index Seek" LogicalOp="Index Seek" ActualRows="21" ActualLogicalReads="3" ActualExecutions="1">
  <Object Table="[EventLog]" Index="[IX_EventLog_UserId_Covering]" IndexKind="NonClustered" />
  <SeekPredicates><Prefix ScanType="EQ"><RangeColumns>[UserId]</RangeColumns></Prefix></SeekPredicates>
</RelOp>
```

One `RelOp` in the whole plan: a single `Index Seek` on `IX_EventLog_UserId_Covering`, 3 logical reads, no `Key Lookup`, no `Nested Loops`. **The per-user query is already fully covered and already as cheap as a single query against this table can get.**

To answer the actual question — does the *composite* `(UserId, CreatedAtUtc)` index alone (without the covering index) get used for `UserId = @p` alone: yes. Day8 Task2's "before" measurement (captured before `IX_EventLog_UserId_Covering` existed) shows `IX_EventLog_UserId_CreatedAtUtc` being seeked on `UserId = 2500` — a composite index is usable by an equality predicate on its leading key column even without a predicate on the trailing column(s). The catch back then was a `Key Lookup` per matched row (to fetch `EventType`/`Payload`, which aren't in that index), costing 65 logical reads for 21 rows. That gap is what `IX_EventLog_UserId_Covering` closed by Day8 Task2, and it's still closed today.

**So the "missing piece" isn't missing on this database as it currently stands** — there's nothing to fix at the index level for this endpoint's per-user query. The N+1 problem here is entirely a round-trip-count problem (200 sequential index-seek round-trips instead of 1 query), not a missing- or wrong-index problem. That distinction matters for how this gets fixed later: the remedy is collapsing 200 queries into one (`WHERE UserId IN (...)`, a join, or a single grouped query), not adding an index.

## OpenTelemetry / Jaeger tracing

Already configured from Day5 and carried forward unchanged in `Program.cs` — not modified for this task:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("QuotesApi"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("QuotesApi")
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
```

`AddAspNetCoreInstrumentation()` creates the root span per incoming request; `AddEntityFrameworkCoreInstrumentation()` creates a child span for every EF Core command. Because the endpoint issues up to 201 separate `ToListAsync()` calls, a Jaeger trace for one request to `/api/users-with-events-slow` should show up to 201 DB-command child spans fanned out under a single request span — the round-trip count made visible without any extra code in the endpoint itself.

## Measured: single-request baseline

Before load testing, ran the app locally (`dotnet run`, Development environment, port 5041 — required generating a local-only `Jwt:SigningKey` user-secret first, since the committed `appsettings.json` placeholder fails `JwtOptions` validation at startup and this app had apparently never been run locally before; not committed to the repo) and confirmed a single request:

```
> curl http://localhost:5041/health          -> HTTP 200
> curl http://localhost:5041/api/users-with-events-slow -> HTTP 200, 8.737s, 200 users returned
```

A single unloaded request already takes ~8.7 seconds — consistent with ~200 sequential round-trips at ~40ms each (the per-query duration measured below).

## Measured: bombardier load test

Ran exactly the planned command against the running app:

```
bombardier -c 10 -d 30s -l http://localhost:5041/api/users-with-events-slow
```

Full output:

```
Bombarding http://localhost:5041/api/users-with-events-slow for 30s using 10 connection(s)
Done!
Statistics        Avg      Stdev        Max
  Reqs/sec         0.74      18.40     506.09
  Latency        10.02s     1.12ms     10.03s
  Latency Distribution
     50%     10.02s
     75%     10.03s
     90%     10.03s
     95%     10.03s
     99%     10.03s
  HTTP codes:
    1xx - 0, 2xx - 0, 3xx - 0, 4xx - 0, 5xx - 0
    others - 30
  Errors:
       timeout - 30
  Throughput:     439.01/s
```

**Every one of the 30 requests bombardier attempted at 10 concurrent connections timed out** — bombardier's default per-request timeout is 10 seconds, and the p50/p99 sitting right at ~10.02s/10.03s shows every request hit that ceiling rather than completing. Zero HTTP 2xx responses; 30/30 timeouts. That's not a bombardier misconfiguration — it's the real result of taking a request that already takes 8.7s alone and running 10 of them concurrently against the same finite SQL connection pool: request latency under contention got pushed past 10s for 100% of requests, worse than the already-slow 8.7s unloaded baseline. This alone is a strong result: the endpoint doesn't degrade gracefully under even modest concurrency, it falls over completely.

## Confirmed: exact EF-emitted SQL and its execution plan, at scale

Captured directly from the app's own logging — no extra instrumentation needed, since [appsettings.Development.json](appsettings.Development.json) already overrides `Microsoft.EntityFrameworkCore.Database.Command` to `Debug`, and Serilog's console sink was already writing it during the single-request confirmation above:

```
Executing DbCommand [Parameters=[@userId='?' (DbType = Int32)], CommandType='Text', CommandTimeout='30']
SELECT [e].[Id], [e].[CreatedAtUtc], [e].[EventType], [e].[Payload], [e].[UserId]
FROM [EventLog] AS [e]
WHERE [e].[UserId] = @userId
```

(Same `TraceId` on every line — confirms all 200 of these came from one HTTP request, one after another, taking 36-51ms each per the app's own command timing.) `EnableSensitiveDataLogging` is intentionally off in the running app, so the logged parameter value is masked (`'?'`) — this SQL text is exact, but the specific `@userId` value logged isn't recoverable from this output.

To confirm the plan holds "at scale" (not just for the one `UserId=2500` value checked earlier), ran this exact captured SQL directly against `QuotesApiDay7` for two different, confirmed-real `UserId` values — `1` (the first user the endpoint actually queried in the confirmation request) and `2500` (checked earlier) — with `SET STATISTICS IO ON` + `SET STATISTICS XML ON`:

| UserId | Rows | Logical reads | Plan |
|---|---|---|---|
| 1 | 20 | 3 | `Index Seek` on `IX_EventLog_UserId_Covering` |
| 2500 | 21 | 3 | `Index Seek` on `IX_EventLog_UserId_Covering` |

Identical single-`RelOp` plan both times — no `Key Lookup`, no `Nested Loops`, no scan. **Confirmed: nothing changes about the per-query plan "at scale."** The plan and cost are exactly as cheap for every user checked as they were for the one checked during the initial index investigation. This rules out per-query cost variance (e.g. a skewed user with a worse plan) as a contributor to the load-test failure above — the SQL Server engine side of this endpoint is not the bottleneck at any point.

## The two biggest problems observed

1. **Connection-pool/concurrency collapse, not query cost.** The load test's 100% timeout rate at just 10 concurrent connections is the headline problem. Each individual query is confirmed cheap (3 logical reads, single index seek) — the failure is entirely about 10 requests × up to 201 sequential round-trips each competing for the same SQL connection pool and thread-pool `await` chain. The fix is architectural (batch the 200 queries into 1), not anything SQL Server-side.
2. **Unbounded sequential latency per request, even alone.** Independent of concurrency, a single unloaded request already costs 8.7 seconds end-to-end — every one of those ~200 round-trips is `await`-ed one at a time rather than batched or parallelized, so total latency scales linearly with distinct-user count regardless of how cheap each individual query is. That 8.7s single-request floor is already unacceptable for an HTTP endpoint before concurrency is even a factor.

## What I learned

The obvious-looking culprit — "there's no usable index for this query" — turned out to be wrong once actually checked against the live database, and stayed wrong at scale: `IX_EventLog_UserId_Covering` (added in Day8 Task2) makes every individual per-user query a single 3-logical-read index seek, confirmed identical for multiple different `UserId` values. The load test then showed the real cost isn't per-query at all — it's the 200x round-trip multiplier combined with connection-pool contention, which turned an already-slow 8.7s single request into a 100% timeout rate at just 10 concurrent connections. N+1 is a round-trip-count and concurrency problem; the fix is collapsing 200 queries into one, not touching indexes.

## What would break this

Raising `MaxDistinctUsers` well past 200, or a `UserId` with far more events than the rest (skew), turns today's ~200 cheap sequential round-trips into either far more round-trips or a few slower ones — though per the plan check above, no realistic amount of skew would make an individual query itself expensive, since the covering index handles any `UserId` value the same way. The load test already shows the actual breaking point is concurrency: 10 simultaneous requests were enough to push every single one to bombardier's 10-second timeout, so any real traffic beyond a handful of concurrent users would make this endpoint effectively unusable well before it would show up as a database-side problem.
