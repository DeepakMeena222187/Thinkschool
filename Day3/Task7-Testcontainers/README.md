# Task 7 — Real SQL Server in CI with Testcontainers

Integration tests for QuotesApi that run against a **real Microsoft SQL Server 2022**
engine via [Testcontainers](https://testcontainers.com/), instead of the in-memory
SQLite used in [Task 6](../Task6-Integration-Tests). Everything else about the
integration-test style carries over unchanged: real `WebApplicationFactory`, real HTTP,
real DI, real middleware, real EF Core, real authentication/authorization.

## Layout

```
Task7-Testcontainers/
  QuotesApi/                                 # Copy of Task 6's app, switched to the SQL Server EF Core provider
  Quotes.Tests.Integration.Testcontainers/   # Integration tests + the Testcontainers fixture
```

`QuotesApi` is a copy, not a reference, of Task 6's project — Task 6 is left untouched.
The only functional changes made to this copy: `Microsoft.EntityFrameworkCore.SqlServer`
replaces `Microsoft.EntityFrameworkCore.Sqlite`/`.InMemory`, `InfrastructureExtensions`
calls `UseSqlServer` instead of `UseSqlite`, and the `Migrations/` folder was
regenerated from scratch against the SQL Server provider (the old SQLite migrations
used SQLite-only column types and an `Sqlite:Autoincrement` annotation that don't
translate to SQL Server).

## SQL Server container lifecycle

Container startup/shutdown is owned by `SqlServerContainerFixture`
(`SqlServerContainerFixture.cs`), an `IAsyncLifetime` fixture shared across every test
class via the `[Collection("SqlServer container")]` / `ICollectionFixture<>` xUnit
mechanism:

- **Startup** — `InitializeAsync()` starts one `mcr.microsoft.com/mssql/server:2022-latest`
  container (via `MsSqlBuilder`) **once** for the entire test run. Testcontainers waits
  for the container's built-in readiness probe before handing control back.
- **Shutdown** — `DisposeAsync()` disposes the container after the last test in the
  run finishes, which stops and removes it.

Starting a fresh container per test would make the suite far too slow (SQL Server
containers take several seconds to become ready), so the server itself is shared for
the whole run. What's *not* shared is data — see [Test isolation](#test-isolation)
below.

## Connection string source

The fixture exposes `ConnectionString`, sourced directly from the running container via
`MsSqlContainer.GetConnectionString()` — the host/port Docker assigned, the SA
credentials Testcontainers generated, nothing hardcoded. `IntegrationTestFactory` takes
that string in its constructor and rewrites only the `Initial Catalog` (database name)
to a per-instance unique value before using it.

`WebApplicationFactory.ConfigureWebHost` then removes whatever `QuotesDbContext`
registration the app would normally set up and replaces it with
`options.UseSqlServer(<per-test connection string>)` — this is the one line that
differs from Task 6, which called `options.UseSqlite(<in-memory connection>)` here
instead.

## Migrations

`IntegrationTestFactory`'s constructor builds a throwaway `QuotesDbContext` against the
per-test database and calls `Database.Migrate()`, applying every migration in
`QuotesApi/Migrations` before the host is built — the same timing Task 6 used, needed
because `Program.cs` seeds the default admin user synchronously during host startup, so
the schema must already exist by then. `DatabaseTests.Database_OnStartup_AppliesAllMigrations`
asserts every declared migration was actually applied and none are pending.

## Test isolation

Every test constructs its **own** `IntegrationTestFactory`, never a shared one:

- The constructor generates a fresh, globally unique database name
  (`QuotesApiTests_<guid>`) on the shared container and migrates it from empty.
- Tests seed whatever rows they need through the real HTTP API or `QuotesDbContext` —
  no test relies on data left behind by another test, and no test assumes the database
  starts non-empty because of another test's side effects.
- `Dispose()` drops the per-test database afterward, so the shared container doesn't
  accumulate one database per test for the run's lifetime.

`DatabaseTests.Database_IsIsolatedAcrossFactoryInstances` exercises this directly:
it creates two factories against the same container, writes through one, and asserts
the other sees nothing.

## What the tests cover

- **Migrations** — `DatabaseTests.Database_OnStartup_AppliesAllMigrations`,
  `Database_OnStartup_SeedsDefaultAdminUser`
- **Quote CRUD** — `QuotesEndpointTests` (create/get/list/delete, paging, 404s)
- **Authentication/authorization** — `AuthEndpointTests` (login, refresh-token rotation
  and reuse detection, logout, expired/malformed tokens), `QuotesEndpointTests` and
  `CollectionEndpointTests` (missing/insufficient scope, ownership checks)
- **Validation/error handling** — invalid quote payloads, undersized collection names,
  duplicate/absent collection items — all asserted as real `ProblemDetails` responses
- **Database persistence** — `DatabaseTests.CreateQuote_ViaApi_PersistsThroughRealEfCoreRoundTrip`,
  `Database_IsIsolatedAcrossFactoryInstances`
- **SQL Server-specific behavior** (`SqlServerSpecificTests.cs`) — a unique index
  enforced by the *server* (`DbUpdateException` on duplicate email, not just an EF Core
  in-memory check), a row read back through a brand-new physical connection/DbContext
  to prove durable server-side storage, and a direct `SELECT @@VERSION` to confirm the
  engine really is SQL Server

## Running locally

Requires **Docker** (Desktop or Engine) running locally — Testcontainers talks to the
Docker daemon to pull and start the SQL Server image.

```bash
cd Day3/Task7-Testcontainers/Quotes.Tests.Integration.Testcontainers
dotnet test
```

The first run pulls `mcr.microsoft.com/mssql/server:2022-latest`, which is a multi-GB
image — expect the first invocation to take noticeably longer than subsequent ones
while Docker downloads and caches it. If Docker isn't running, the fixture fails fast
with a clear `Docker is either not running or misconfigured` error rather than hanging.

## CI

`.github/workflows/day3-task7-testcontainers.yml` (repo root) runs this suite on
`ubuntu-latest` GitHub-hosted runners, which ship with Docker already installed and
running — no self-hosted runner and no manually started SQL Server instance required.
The workflow builds the project, runs `dotnet test` with a `.trx` logger, and publishes
the results as a check run (via `dorny/test-reporter`) plus a downloadable artifact.
