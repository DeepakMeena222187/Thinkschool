# Change Tracker Benchmark

Same `QuotesApi` app as [Day9/Task2-Deadlock/QuotesApi](../../../Day9/Task2-Deadlock/QuotesApi) (no other feature changes) — this task reuses `dbo.EventLog` on the `QuotesApiDay7` Azure SQL database, already created and seeded with 100,000 rows back in Day8. Nothing here recreates or reseeds `EventLog`; it only adds an EF Core mapping onto the existing table and a benchmark that reads through it.

The goal is to show, with real numbers, what EF Core's change tracker actually costs: identity resolution (why two tracked queries for the same row return the same object) and the time/allocation overhead of tracking versus `AsNoTracking()` at read-only scale.

## Files

| File | What it shows |
|---|---|
| [Models/EventLog.cs](Models/EventLog.cs) | Minimal entity mapped onto the existing `dbo.EventLog` table |
| [Data/QuotesDbContext.cs](Data/QuotesDbContext.cs) | Fluent API mapping for `EventLog` — no migration, the table already exists |
| [Benchmarks/ChangeTrackerBenchmark.cs](Benchmarks/ChangeTrackerBenchmark.cs) | Identity resolution demo + tracked vs. `AsNoTracking()` timing/allocation benchmark |
| [Program.cs](Program.cs) | `--benchmark` flag runs the benchmark and exits instead of starting the API |

## What identity resolution is

When a `DbContext` tracks entities (the default), it keeps a map from primary key to the CLR object it already materialized for that row. If you query for the same row twice against the **same** `DbContext` instance, EF Core's change tracker recognizes the key is already tracked and hands back the **same object reference** instead of materializing a second one from the row data — that's identity resolution. It only happens with tracking, because it depends on the change tracker's per-context map of key → instance; `AsNoTracking()` never populates that map, so each query always materializes a fresh object and two queries for the same row return two distinct references.

The benchmark's identity resolution demo (`ChangeTrackerBenchmark.RunIdentityResolutionDemoAsync`) makes this concrete: querying `EventLogs.FirstAsync(e => e.Id == 1)` twice on one tracked context prints `ReferenceEquals(first, second) = True`; repeating it with `AsNoTracking()` prints `False`.

## What `AsNoTracking()` skips, and why it costs time/memory at scale

With tracking on, for every entity a query materializes, EF Core's change tracker:

- Adds an entry to its internal identity map (key → entity instance), which is what makes identity resolution possible.
- Takes a **snapshot** of the entity's original property values, so a later `SaveChanges()` can diff current vs. original and know what changed.
- Wires up navigation-fixup bookkeeping so related tracked entities can find each other.

All of that is pure overhead when you only intend to read data and never call `SaveChanges()` on it. `AsNoTracking()` skips the identity map insert and the original-values snapshot entirely — each row becomes a plain object with no tracker entry. At small result sets the difference is noise; at the scale this benchmark reads (10,000 rows out of `EventLog`'s 100,000), the per-entity bookkeeping adds up into measurable extra time and heap allocation, since tracking allocates a snapshot object per entity in addition to the entity itself.

## When NOT to use `AsNoTracking()`

Don't use it whenever you intend to modify the entities you're reading and call `SaveChanges()` on them. An untracked entity has no change-tracker entry and no original-values snapshot, so EF Core has nothing to diff — property changes you make on it are silently invisible to `SaveChanges()` and never reach the database. `AsNoTracking()` is only correct for read-only paths.

## Running the benchmark

```
dotnet run -- --benchmark
```

This requires `ConnectionStrings:Quotes` to resolve to the real `QuotesApiDay7` Azure SQL Database. The placeholder in [appsettings.json](appsettings.json) points at a local `QuotesApi` database with `Password=CHANGE_ME`; this project's `UserSecretsId` is the same one already used across Day8/Day9, where `ConnectionStrings:Quotes` was already overridden via user-secrets to point at `QuotesApiDay7` — not committed to the repo.

## Measured results

**Measured on 2026-08-20** against `QuotesApiDay7` (`EventLog` at 100,000+ rows), via `dotnet run -- --benchmark`.

### Identity resolution demo

- Tracked, same `DbContext`, two queries for `Id == 1`: `ReferenceEquals(first, second)` = **True**
- `AsNoTracking()`, same `DbContext`, two queries for `Id == 1`: `ReferenceEquals(first, second)` = **False**

### Timing/allocation — reading 10,000 `EventLog` rows

| Variant | Elapsed (ms) | Allocated (bytes) |
|---|---|---|
| Tracked (default) | 833.1 | 9,621,112 |
| `AsNoTracking()` | 258.0 | 3,811,288 |

- Elapsed delta: **575.1 ms** (**69.0%** faster with `AsNoTracking()`)
- Allocation delta: **5,809,824 bytes** (**60.4%** less allocated with `AsNoTracking()`)

### A methodology note: per-thread allocation counters and `await`

The first run of this benchmark used `ToListAsync()` inside the measured span and produced a nonsensical **negative** allocation delta for the `AsNoTracking()` variant. `GC.GetAllocatedBytesForCurrentThread()` is a per-thread counter, and an awaited call can resume its continuation on a different thread-pool thread — so the "after" reading came from a different thread's counter than "before," producing garbage. The fix (see [Benchmarks/ChangeTrackerBenchmark.cs](Benchmarks/ChangeTrackerBenchmark.cs)) was to use the synchronous `ToList()` for the bracketed `allocBefore`/`allocAfter` span specifically, so no thread hop can occur between the two readings. The numbers above are from that corrected run.
