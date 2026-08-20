# Query Translation

Same `QuotesApi` app as [Day10/Task1-ChangeTrackerBenchmark/QuotesApi](../../Task1-ChangeTrackerBenchmark/QuotesApi) (no other feature changes) — this task reuses `dbo.EventLog` on the `QuotesApiDay7` Azure SQL database, already created and seeded with 100,000 rows back in Day8. Nothing here recreates or reseeds `EventLog`; it only demonstrates how EF Core translates LINQ into SQL, and what happens when it can't.

## Files

| File | What it shows |
|---|---|
| [Models/EventLog.cs](Models/EventLog.cs) | Minimal entity mapped onto the existing `dbo.EventLog` table |
| [Data/QuotesDbContext.cs](Data/QuotesDbContext.cs) | Fluent API mapping for `EventLog` — no migration, the table already exists |
| [Contracts/EventLogSummaryDto.cs](Contracts/EventLogSummaryDto.cs) | Projection target for the leaner, `Select()`-based query |
| [QueryTranslation/QueryTranslationDemo.cs](QueryTranslation/QueryTranslationDemo.cs) | Full-entity vs. projected query, a client-evaluation failure, and its fix |
| [Program.cs](Program.cs) | `--querydemo` flag runs the demo and exits instead of starting the API |

## `LogTo` / `EnableSensitiveDataLogging` — dev-only diagnostics

`DbContextOptionsBuilder.LogTo(Console.WriteLine, LogLevel.Information)` streams EF Core's internal logging — including the generated SQL for every query — straight to the console. `EnableSensitiveDataLogging()` additionally puts actual parameter *values* (not just parameter placeholders) into that logged SQL, which is what makes the console output show real `EventType`/`CreatedAtUtc` values instead of `@__e_0`.

Both are appropriate only for a local diagnostic run like this one:

- `EnableSensitiveDataLogging` is documented by Microsoft as unsafe for production because parameter values (which can include personal or sensitive data) end up in log output. It's fine here because this run is against non-production data and the output goes to a local console, not a shipped log sink.
- `LogTo(Console.WriteLine, ...)` bypasses the app's normal structured logging (Serilog) entirely — it's a standalone diagnostic hook, not something that should be wired into the running API.

Neither is enabled anywhere in the normal app startup path — only inside `QueryTranslationDemo.RunAsync`, which is only reached behind the explicit `--querydemo` flag.

## Running the demo

```
dotnet run -- --querydemo
```

This requires `ConnectionStrings:Quotes` to resolve to the real `QuotesApiDay7` Azure SQL Database, the same way [Task1](../../Task1-ChangeTrackerBenchmark/QuotesApi/README.md) does — via user-secrets under this project's `UserSecretsId`, not the committed placeholder in `appsettings.json`.

## Generated SQL and behavior

**Measured on 2026-08-20** against `QuotesApiDay7` (`EventLog` at 100,000+ rows), via `dotnet run -- --querydemo`.

### Full-entity query

`EventLogs.Where(e => e.EventType == "Login").Take(100).ToListAsync()` pulls every column of `EventLog` for each matching row.

Generated SQL:

```sql
Executed DbCommand (119ms) [Parameters=[@p='100'], CommandType='Text', CommandTimeout='30']
SELECT TOP(@p) [e].[Id], [e].[CreatedAtUtc], [e].[EventType], [e].[Payload], [e].[UserId]
FROM [EventLog] AS [e]
WHERE [e].[EventType] = N'Login'
```

Returned 100 rows. EF Core also logged a `RowLimitingOperationWithoutOrderByWarning` here — `Take(100)` without an `OrderBy` means which 100 rows come back is server-determined and not guaranteed stable across runs.

### Projected query

`EventLogs.Where(e => e.EventType == "Login").Select(e => new EventLogSummaryDto {...}).Take(100).ToListAsync()` generates SQL that selects only `Id`, `UserId`, `CreatedAtUtc` instead of every column.

Generated SQL:

```sql
Executed DbCommand (53ms) [Parameters=[@p='100'], CommandType='Text', CommandTimeout='30']
SELECT TOP(@p) [e].[Id], [e].[UserId], [e].[CreatedAtUtc]
FROM [EventLog] AS [e]
WHERE [e].[EventType] = N'Login'
```

Returned 100 rows. Side by side, the column list drops from `Id, CreatedAtUtc, EventType, Payload, UserId` to just `Id, UserId, CreatedAtUtc` — `EventType` (redundant, it's the filter) and `Payload` (the widest column) are never sent over the wire, which is the whole point of projecting instead of loading the full entity.

### Client-side evaluation attempt

`EventLogs.Where(e => IsRecentEvent(e.CreatedAtUtc))` calls a local static method inside the `Where()` predicate. EF Core has no way to translate an arbitrary CLR method call to SQL.

Actual exception observed:

```
System.InvalidOperationException: The LINQ expression 'DbSet<EventLog>()
    .Where(e => QueryTranslationDemo.IsRecentEvent(e.CreatedAtUtc))' could not be translated. Additional information: Translation of method 'QuotesApi.QueryTranslation.QueryTranslationDemo.IsRecentEvent' failed. If this method can be mapped to your custom function, see https://go.microsoft.com/fwlink/?linkid=2132413 for more information. Either rewrite the query in a form that can be translated, or switch to client evaluation explicitly by inserting a call to 'AsEnumerable', 'AsAsyncEnumerable', 'ToList', or 'ToListAsync'.
```

No SQL was logged for this query — it never reached the database. This confirms that on EF Core 10, an untranslatable predicate is a hard failure at query-execution time, not a silent fallback to in-memory (client-side) evaluation the way older EF/EF Core versions used to behave.

### The fix

`EventLogs.Where(e => e.CreatedAtUtc > DateTime.UtcNow.AddDays(-7))` expresses the same "recent event" intent using only expressions EF Core can translate (a computed `DateTime` cutoff compared with `>`).

Generated SQL:

```sql
Executed DbCommand (35ms) [Parameters=[@p='10', @cutoff='2026-08-13T05:36:53.8089481Z'], CommandType='Text', CommandTimeout='30']
SELECT TOP(@p) [e].[Id], [e].[CreatedAtUtc], [e].[EventType], [e].[Payload], [e].[UserId]
FROM [EventLog] AS [e]
WHERE [e].[CreatedAtUtc] > @cutoff
```

Succeeded, returned 10 rows. `DateTime.UtcNow.AddDays(-7)` is evaluated client-side once (into the `@cutoff` parameter) before the query runs, then the `>` comparison itself is pushed into the `WHERE` clause — the difference from the failing version is that nothing here requires EF Core to translate a *method call* into SQL, only a parameter value and a comparison operator it already knows how to translate.

## What I learned

The failure mode isn't "EF Core silently reads everything into memory and filters there" (that was pre-3.0 EF Core behavior) — on EF Core 10 an untranslatable predicate throws `InvalidOperationException` immediately, with a message that names the exact method that couldn't be translated, which makes the fix obvious without needing to inspect the SQL log at all.

## What would break this

Renaming or retyping a column projected in `EventLogSummaryDto` (e.g. `EventLog.UserId` changing type) without updating the DTO would break the projected query's mapping. Separately, a predicate that *looks* translatable but calls something EF Core's SQL Server provider doesn't recognize — e.g. a custom extension method, a regex match, or `string.Contains` on a locale-sensitive overload — would reproduce the same `InvalidOperationException` seen above.
