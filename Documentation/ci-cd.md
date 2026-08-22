# CI/CD

## Current state: CI already exists

Continuous integration was added to this repository in commit `5ea0423`
("Day 4 task 1: wire CI with GitHub Actions") as [.github/workflows/ci.yml](../.github/workflows/ci.yml).
It is already active on `main` and has already run successfully against the
Day 11 and Day 12 commits (build + test, all green in GitHub Actions history).
This document describes that existing pipeline; no new workflow was added.

A second, narrowly-scoped workflow,
[.github/workflows/day3-task7-testcontainers.yml](../.github/workflows/day3-task7-testcontainers.yml),
also exists for the Docker/Testcontainers-based SQL Server integration tests
under `Day3/Task7-Testcontainers`.

## When it runs

- On every `push` to any branch.
- On every `pull_request` targeting `main`.

## What it validates

The repository has no single top-level `.sln` — each `DayN/TaskX-*` folder is
a self-contained snapshot, and several folders reuse the same project names
(e.g. `QuotesApi`), so one generated solution file cannot hold them all. The
workflow instead discovers every `*.csproj` in the repo and, for each one:

1. Runs `dotnet restore`.
2. Runs `dotnet build --configuration Release`.
3. If the project references `Microsoft.NET.Test.Sdk` (i.e. it is an actual
   test project), runs `dotnet test` under `dotnet-coverage` and publishes
   TRX results and Cobertura coverage as workflow artifacts.
4. Merges coverage across all test projects and fails the run if aggregate
   line coverage drops below 70%.

This means Day 11 (`Task1-ProfileSlowEndpoint`, `Task2-FixSlowEndpoint`) and
Day 12 (`Task1-CqrsLite`, `Task2-DapperComparison`) are picked up
automatically: their `QuotesApi.csproj` files are restored and built on every
push/PR. Neither Day has a dedicated test project, so CI correctly builds
them without running (or fabricating) any tests — consistent with how every
other test-less project in the repo is already treated.

Verified locally (same commands CI runs, `dotnet` SDK 10.0.400):

```
dotnet restore Day11/Task1-ProfileSlowEndpoint/QuotesApi/QuotesApi.csproj
dotnet build   Day11/Task1-ProfileSlowEndpoint/QuotesApi/QuotesApi.csproj --no-restore --configuration Release
dotnet restore Day11/Task2-FixSlowEndpoint/QuotesApi/QuotesApi.csproj
dotnet build   Day11/Task2-FixSlowEndpoint/QuotesApi/QuotesApi.csproj --no-restore --configuration Release
dotnet restore Day12/Task1-CqrsLite/QuotesApi/QuotesApi.csproj
dotnet build   Day12/Task1-CqrsLite/QuotesApi/QuotesApi.csproj --no-restore --configuration Release
dotnet restore Day12/Task2-DapperComparison/QuotesApi/QuotesApi.csproj
dotnet build   Day12/Task2-DapperComparison/QuotesApi/QuotesApi.csproj --no-restore --configuration Release
```

All four builds succeeded with 0 warnings / 0 errors.

## Existing Day/Task work was preserved

No Day11 or Day12 files, commits, or evidence were changed, moved, or
rewritten as part of this review. This document only records what the
existing CI pipeline already does.

## What remains for CD

No deployment workflow exists for Day 11 or Day 12 today, and none was added.
The only deployment-shaped artifact in the repo is
[Day5/Task4-Azd/azure.yaml](../Day5/Task4-Azd/azure.yaml) with its
accompanying Bicep files under `Day5/Task4-Azd/infra/` — that scaffolding
targets the Day 5 container exercise specifically and is not wired to any
GitHub Actions workflow or to Day 11/Day 12.

To stand up real CD for a target app (e.g. Day 12's `QuotesApi`), the
following would need to exist first and would need explicit sign-off before
any of it is created:

- A concrete deployment target: an actual Azure resource group / App
  Service / Container App, decided with the user — not auto-provisioned.
- Azure credentials for GitHub Actions to authenticate (e.g. an OIDC federated
  credential + `azure/login`, or a publish profile), stored as GitHub
  Encrypted Secrets — never committed to the repo.
- A decision on deployment trigger (e.g. only on merge to `main`, or manual
  `workflow_dispatch`) and environment protection rules if there's more than
  one stage (dev/staging/prod).
- Infrastructure-as-code for the target environment (the `Day5/Task4-Azd`
  Bicep could potentially be adapted, but that's a separate scoped task).

Until those are confirmed, no cloud resources, secrets, or deployment
workflow should be created.

