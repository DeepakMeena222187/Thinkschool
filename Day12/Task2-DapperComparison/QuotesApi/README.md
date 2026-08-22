# Dapper vs EF Core: Same Query, Two Implementations

Same `QuotesApi` app as [Day12/Task1-CqrsLite/QuotesApi](../../Task1-CqrsLite/QuotesApi), copied unchanged (minus `bin`/`obj`) and extended with a second, Dapper-backed implementation of the exact same read: "list quotes, newest first, flagged by whether the current user owns each one." Everything from Task1 — `EventLog`, the non-CQRS `/api/quotes` endpoints, the EF Core `GetQuoteListQuery`/`CreateQuoteCommand` — is untouched. This task is additive: both the EF Core and Dapper paths are reachable side by side for comparison.

## Files

| File | What it does |
|---|---|
| [Features/Quotes/GetQuoteListQueryDapperHandler.cs](Features/Quotes/GetQuoteListQueryDapperHandler.cs) | New — `GetQuoteListQueryDapper` + `GetQuoteListQueryDapperHandler`, the Dapper read path |
| [Extensions/CqrsQuoteEndpointExtensions.cs](Extensions/CqrsQuoteEndpointExtensions.cs) | `GET /api/cqrs/quotes/dapper` added alongside the existing `GET /api/cqrs/quotes` (EF Core) |
| [Benchmarks/DapperComparisonBenchmark.cs](Benchmarks/DapperComparisonBenchmark.cs) | New — runs both paths 20x each against the same data, reports avg elapsed ms + avg allocated bytes |
| [Program.cs](Program.cs) | `--dappercompare` flag runs the benchmark and exits instead of starting the API (same convention as Day10's `--benchmark`) |
| [QuotesApi.csproj](QuotesApi.csproj) | `Dapper` 2.1.79 and `Microsoft.Data.SqlClient` 7.0.2 added via `dotnet add package` |

## Why a second request type, not a second handler

MediatR resolves exactly one handler per request type from DI. Registering a second `IRequestHandler<GetQuoteListQuery, List<QuoteListItemDto>>` alongside Task1's EF Core one wouldn't give two reachable paths — whichever handler the container resolves last would silently shadow the other, and which one "wins" isn't something to depend on. `GetQuoteListQueryDapper` is a distinct `IRequest<List<QuoteListItemDto>>` with an identical shape (same `CurrentUserId` in, same `List<QuoteListItemDto>` out), so both `mediator.Send(new GetQuoteListQuery(...))` and `mediator.Send(new GetQuoteListQueryDapper(...))` are independently reachable and the comparison stays apples-to-apples on both request and response.

## Side by side

**EF Core** ([Day12/Task1-CqrsLite/QuotesApi/Features/Quotes/GetQuoteListQuery.cs](../../Task1-CqrsLite/QuotesApi/Features/Quotes/GetQuoteListQuery.cs), unchanged):

```csharp
public sealed class GetQuoteListQueryHandler(QuotesDbContext db) : IRequestHandler<GetQuoteListQuery, List<QuoteListItemDto>>
{
    public Task<List<QuoteListItemDto>> Handle(GetQuoteListQuery request, CancellationToken cancellationToken)
    {
        return db.Quotes
            .AsNoTracking()
            .OrderByDescending(q => q.CreatedAtUtc)
            .Select(q => new QuoteListItemDto(
                q.Id, q.Author, q.Text, q.CreatedAtUtc, q.OwnerId == request.CurrentUserId))
            .ToListAsync(cancellationToken);
    }
}
```

EF Core translates that whole LINQ expression — including the `OwnerId == CurrentUserId` comparison — into SQL. The generated shape is effectively:

```sql
SELECT [q].[Id], [q].[Author], [q].[Text], [q].[CreatedAtUtc],
       CASE WHEN [q].[OwnerId] = @__request_CurrentUserId_0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
FROM [Quotes] AS [q]
ORDER BY [q].[CreatedAtUtc] DESC
```

**Dapper** ([Features/Quotes/GetQuoteListQueryDapperHandler.cs](Features/Quotes/GetQuoteListQueryDapperHandler.cs), new):

```csharp
public sealed class GetQuoteListQueryDapperHandler(IConfiguration configuration) : IRequestHandler<GetQuoteListQueryDapper, List<QuoteListItemDto>>
{
    private const string Sql = """
        SELECT Id, Author, Text, CreatedAtUtc,
               CASE WHEN OwnerId = @CurrentUserId THEN 1 ELSE 0 END AS IsOwnedByCurrentUser
        FROM Quotes
        ORDER BY CreatedAtUtc DESC
        """;

    public async Task<List<QuoteListItemDto>> Handle(GetQuoteListQueryDapper request, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Quotes")
            ?? throw new InvalidOperationException("Missing required configuration value: ConnectionStrings:Quotes.");

        using var connection = new SqlConnection(connectionString);

        var quotes = await connection.QueryAsync<QuoteListItemDto>(Sql, new { CurrentUserId = request.CurrentUserId });

        return quotes.AsList();
    }
}
```

The SQL is hand-written and identical in intent to what EF Core generates, aliased so the column names (`Id`, `Author`, `Text`, `CreatedAtUtc`, `IsOwnedByCurrentUser`) match `QuoteListItemDto`'s constructor parameters exactly — Dapper maps each result row to a new `QuoteListItemDto` via that constructor since the record has no public setters. The connection string comes from the same `IConfiguration` / `ConnectionStrings:Quotes` that `QuotesDbContext` uses (via `builder.Services.AddInfrastructure(...)` — see [Extensions/InfrastructureExtensions.cs](Extensions/InfrastructureExtensions.cs)), not hardcoded, and a fresh `SqlConnection` is opened per call rather than sharing `QuotesDbContext`'s connection — Dapper has no `DbContext`-equivalent scope to reuse here, and pooling happens underneath at the ADO.NET connection-pool level regardless.

## Running the comparison

```
dotnet run -- --dappercompare
```

Requires `ConnectionStrings:Quotes` to resolve to the real `QuotesApiDay7` Azure SQL Database (same as Task1 — the placeholder in [appsettings.json](appsettings.json) points at a local, non-existent `QuotesApi` database; the real value comes from a secrets store outside this repo).

The benchmark ([Benchmarks/DapperComparisonBenchmark.cs](Benchmarks/DapperComparisonBenchmark.cs)) runs each path once as a warm-up (JIT, connection pool, SQL Server plan cache — not measured), then 20 measured iterations each, back-to-back, against the same live `Quotes` table and the same `CurrentUserId`. Each iteration wraps a `Stopwatch` and a `GC.GetAllocatedBytesForCurrentThread()` before/after snapshot around a **synchronous** call — `ToList()` for EF Core, `Query<T>()` (not `QueryAsync<T>`) for Dapper — deliberately mirroring the fix [Day10/Task1-ChangeTrackerBenchmark](../../../Day10/Task1-ChangeTrackerBenchmark/QuotesApi/README.md) needed: `GC.GetAllocatedBytesForCurrentThread()` is a per-thread counter, and an `await`ed call can resume its continuation on a different thread-pool thread, making the "before" and "after" readings come from two different threads' counters and producing a nonsensical (even negative) delta. Keeping the measured span synchronous guarantees both readings are on the calling thread. The real handlers used by the API (`ToListAsync`/`QueryAsync`) stay async, as they should for a web request — only the benchmark's measured bracket avoids `await`.

## Measured results

Ran via `dotnet run -- --dappercompare` against the live `QuotesApiDay7` Azure SQL database (checked 2026-08-22):

```
=== EF Core vs Dapper: GetQuoteListQuery Benchmark ===
20 iterations each, CurrentUserId=1, same live data for both

EF Core (LINQ projection): 12 rows, avg 40.91 ms over 20 runs, avg 168,425 bytes allocated/run
Dapper (raw SQL): 12 rows, avg 35.05 ms over 20 runs, avg 13,022 bytes allocated/run

--- Summary ---
Variant                        Avg elapsed (ms)  Avg allocated (bytes)
EF Core (LINQ projection)                 40.91                168,425
Dapper (raw SQL)                          35.05                 13,022

Dapper saved 5.86 ms (14.3%) and 155,403 bytes (92.3%) versus EF Core, per call.
```

| Variant | Avg elapsed (ms) | Avg allocated (bytes/run) |
|---|---|---|
| EF Core (LINQ projection) | 40.91 | 168,425 |
| Dapper (raw SQL) | 35.05 | 13,022 |

Measured against 12 rows in `Quotes` (the 10 originally seeded plus 2 created in earlier API testing of this endpoint). Dapper is ~14.3% faster on elapsed time and allocates ~92.3% fewer bytes per call at this table size.

**One bug this surfaced:** the SQL as originally written (`CASE WHEN OwnerId = @CurrentUserId THEN 1 ELSE 0 END AS IsOwnedByCurrentUser`, no cast) threw `InvalidOperationException` from Dapper at query time — "no constructor found." SQL Server infers a bare `1`/`0` `CASE` as `int`, but `QuoteListItemDto.IsOwnedByCurrentUser` is `bool`, and Dapper's constructor-based materialization for records requires an exact per-parameter type match, not an implicit int→bool coercion. Fixed by wrapping the `CASE` in `CAST(... AS BIT)` in both [Features/Quotes/GetQuoteListQueryDapperHandler.cs](Features/Quotes/GetQuoteListQueryDapperHandler.cs) and the benchmark's copy of the SQL — EF Core's LINQ projection never hit this because `q.OwnerId == request.CurrentUserId` is typed as `bool` in C# from the start, so EF Core generates the `CAST(... AS bit)` itself.

**API evidence — same request, both endpoints, byte-for-byte identical response:**

`GET /api/cqrs/quotes?currentUserId=1` (EF Core) and `GET /api/cqrs/quotes/dapper?currentUserId=1` (Dapper) both returned:

```json
[{"id":12,"author":"Ada Lovelace","text":"CQRS test quote 2","createdAtUtc":"2026-08-22T04:46:02.6489946","isOwnedByCurrentUser":true},{"id":11,"author":"Ada Lovelace","text":"CQRS test quote","createdAtUtc":"2026-08-22T04:33:29.4132847","isOwnedByCurrentUser":true},{"id":9,"author":"Alan Turing","text":"A computer would deserve to be called intelligent if it could deceive a human into believing it was human.","createdAtUtc":"2026-08-02T09:50:00","isOwnedByCurrentUser":true},{"id":6,"author":"Grace Hopper","text":"One accurate measurement is worth a thousand expert opinions.","createdAtUtc":"2026-07-30T16:00:00","isOwnedByCurrentUser":true},{"id":3,"author":"Ada Lovelace","text":"Mathematical science shows what is. It is the language of the unseen relations between things.","createdAtUtc":"2026-06-20T18:15:00","isOwnedByCurrentUser":true},{"id":8,"author":"Alan Turing","text":"Sometimes it is the people no one imagines anything of who do the things that no one can imagine.","createdAtUtc":"2026-05-14T13:10:00","isOwnedByCurrentUser":true},{"id":5,"author":"Grace Hopper","text":"A ship in port is safe, but that is not what ships are built for.","createdAtUtc":"2026-04-18T08:45:00","isOwnedByCurrentUser":true},{"id":10,"author":"Katherine Johnson","text":"Like what you do, and then you will do your best.","createdAtUtc":"2026-03-29T12:00:00","isOwnedByCurrentUser":true},{"id":2,"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal.","createdAtUtc":"2026-03-11T14:30:00","isOwnedByCurrentUser":true},{"id":4,"author":"Grace Hopper","text":"The most dangerous phrase in the language is: We've always done it this way.","createdAtUtc":"2026-02-02T10:00:00","isOwnedByCurrentUser":true},{"id":7,"author":"Alan Turing","text":"We can only see a short distance ahead, but we can see plenty there that needs to be done.","createdAtUtc":"2026-01-25T11:20:00","isOwnedByCurrentUser":true},{"id":1,"author":"Ada Lovelace","text":"The Analytical Engine has no pretensions whatever to originate anything.","createdAtUtc":"2026-01-05T09:00:00","isOwnedByCurrentUser":true}]
```

Confirmed by a direct string comparison of both responses (identical, not just identically-shaped) — same 12 quotes, same order, same `isOwnedByCurrentUser: true` for every row (all owned by `currentUserId=1`, including the two test quotes created against Task1's CQRS endpoint earlier).

## When to reach for Dapper vs EF Core

Default to EF Core for anything that writes, needs change tracking, or materializes a non-trivial object graph (navigations, owned types like `Collection.Items`) — that's exactly the machinery `CreateQuoteCommandHandler` in Task1 leans on, and hand-rolling it in Dapper would mean reimplementing what EF Core already does correctly. Reach for Dapper on hot, simple, read-only paths where the result shape is already flat (a DTO, not an entity graph) and the query is simple enough to hand-tune directly — there, Dapper skips LINQ-to-SQL translation and change-tracker bookkeeping entirely, at the cost of maintaining the SQL by hand (including its column types matching the DTO exactly — see the `CAST(... AS BIT)` bug below) and losing EF Core's automatic query translation if the shape ever changes. The measured numbers above partially support this at only 12 rows: the ~92% allocation reduction is a genuine, network-independent win for Dapper on this exact read, while the ~14% elapsed-time difference is likely mostly network round-trip noise at this table size and needs re-measuring at a larger row count (see "what would break this") before it's a real data point either way.

## What I learned

The allocation gap (168,425 vs 13,022 bytes — Dapper allocates ~8x less) is the more trustworthy number of the two at this row count: it's dominated by EF Core's change-tracking/query-pipeline machinery even with `AsNoTracking()` (expression tree compilation caching, entity materialization scaffolding) versus Dapper's much thinner reflection-emitted deserializer, and that gap doesn't depend on network conditions. The elapsed-time gap (40.91 vs 35.05 ms, 14.3%) is far more likely dominated by the round trip to Azure SQL over the local network for both paths at only 12 rows — in-process LINQ-translation/tracking overhead is real but small next to a network round trip this size, so that particular percentage shouldn't be trusted as "Dapper is 14% faster" in general; it would need re-measuring at a row count large enough that in-process overhead, not network latency, dominates the wall-clock time (see below).

## What would break this

At 12 rows, both numbers are close enough to network/connection variance that re-running the benchmark could shift the elapsed-ms delta noticeably or even flip which one looks faster on a given run — this comparison needs re-running against a table with thousands of rows (like `EventLog` in Day8-11) before treating the elapsed-time percentage as real. Also, the Dapper path has no compile-time safety net the EF Core path has: a column rename or type change on `Quotes` breaks `GetQuoteListQueryDapperHandler` at runtime with a mapping exception (as this task's own `CASE`-without-`CAST` bug demonstrated - Dapper's constructor-matching failed at query time, not build time), where EF Core either adapts automatically via the entity model or fails to compile if the LINQ no longer lines up with `Quote`'s properties.
