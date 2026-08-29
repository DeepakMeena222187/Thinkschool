# Day 17 — Deploy to Azure Static Web Apps

Branch: `day17/azure-swa-deploy`. This file grows through Step A → B → C → D.

## Design decisions (approved before any build step)

- **API compute**: App Service, Free F1 tier, Central India (co-located with
  `quotesapi-day7-sql`). Cost: $0/month.
  - **Known ceiling, not a hidden one**: F1 has no "Always On" (cold start
    after ~20 min idle) and a **hard 60 CPU-minute/day cap** — if exceeded,
    Azure suspends the app for the rest of the day. Acceptable for this
    project's traffic; the fix if it's ever hit is a one-line upgrade to B1
    (~$13/month), not a redesign.
- **No Standard-tier SWA, no linked backend.** Free-tier SWA + CORS is the
  chosen trade at this budget. Consequence: the browser calls the API's
  real hostname directly (cross-origin), so CORS has to allow the deployed
  SWA origin, and neither the API's hostname (frontend) nor the allowed
  CORS origin (backend) can be hardcoded in source — both get injected at
  deploy time from GitHub Actions variables (Step C/D).
- **SWA region**: East Asia. Static Web Apps isn't available in Central
  India (verified: only Central US, East US 2, West US 2, West Europe,
  East Asia support it) — East Asia is the closest supported region to
  India among those five.
- **Managed identity, scoped to `db_datareader` + `db_datawriter` only.**
  No `db_ddladmin`. Consequence: `MigrateAsync()` cannot run under this
  identity - see below.
- **GitHub Actions authenticates to Azure via OIDC federation** - no
  stored Azure credential of any kind in the repo or workflow.

## Fixed before Step A, found while preparing it (not part of the original ask)

Two things in `Day5/Task6-Resilience/QuotesApi/Program.cs` needed to change
before deploying anything, both discovered while implementing the
MI-permission-scope decision:

1. **`db.Database.MigrateAsync()` no longer runs in Production.** The guard
   at Program.cs's startup block was `if (!app.Environment.IsEnvironment("Testing"))`;
   extended to `&& !app.Environment.IsProduction()`. Chose **environment-
   conditional**, not a separate feature flag - it's a one-clause extension
   of a guard that already existed for the same reason (Testing), not a new
   mechanism. Migrations now run manually, under an Entra admin identity,
   against the real database - never automatically, and never under the
   MI's more limited permissions.
2. **`SeedDevelopmentUserAsync` had no environment guard at all** - unlike
   its sibling `SeedDevelopmentCollectionsAsync`, which already checked
   `IsDevelopment()`. Unfixed, it would have run in every environment,
   including this deployment, seeding a login with a **hardcoded plaintext
   password** (`meena@123`, visible in source) into the live database if
   that email didn't already exist. Fixed to match its sibling's existing
   pattern exactly - not something asked for directly, but the same
   category of bug, adjacent code, found while reading the block that was
   asked about. Both changes verified with a clean `dotnet build` (0
   warnings, 0 errors) before touching Azure.

## Step A — App Service + managed identity ✅

| | |
|---|---|
| Resource group | `rg-quotesapi-day7` (existing, unchanged - reused rather than creating a new one) |
| App Service Plan | `quotesapi-day7-plan` — Linux, **F1 (Free)**, Central India |
| Web App | **`quotesapi-day7-api`** |
| Hostname | `quotesapi-day7-api.azurewebsites.net` |
| Runtime | `.NET 10.0 (LTS)` — verified as an actively-supported Azure App Service stack (`DOTNETCORE\|10.0`, support status "Active", EOL 2028-12-01) before choosing it, not assumed |
| System-assigned MI | **Enabled.** Principal ID `53ec64e1-be96-40ba-b7a2-6930e4f7d8b5` |

**Resource name for the `CREATE USER` T-SQL: `quotesapi-day7-api`**

```sql
CREATE USER [quotesapi-day7-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [quotesapi-day7-api];
ALTER ROLE db_datawriter ADD MEMBER [quotesapi-day7-api];
```

No code deployed yet (that's Step B) - the app currently serves nothing at
its hostname. A reachability check right after creation timed out, which
is expected for a brand-new F1 app with no deployed content, not a fault.

## Security fixes made before any deployment

Two real bugs, found while preparing the code for a live audience rather
than local dev - neither was asked for directly, both are the same class
of problem (a dev-only shortcut with no environment guard, safe until the
first production deploy exposes it).

1. **`SeedDevelopmentUserAsync` had no environment guard at all** - unlike
   its sibling `SeedDevelopmentCollectionsAsync`, which already checked
   `IsDevelopment()`. Unfixed, it would run in every environment,
   including this deployment, seeding a login with a **hardcoded
   plaintext password** (`meena@123`) into whatever database it's pointed
   at. Fixed twice, not once:
   - Added the missing `IsDevelopment()` guard (matches its sibling's
     existing pattern).
   - Removed the hardcoded fallback entirely. `QUOTES_API_DEV_EMAIL` and
     `QUOTES_API_DEV_PASSWORD` now have no default - if either is unset,
     seeding is skipped, rather than falling back to a known credential.
   - Separately investigated and reported (not fixed - explicitly out of
     scope, read-only): that same plaintext password is reachable from
     29 commits and 40 files across ~20 historical day-folder snapshots,
     pushed to both `origin` and `thinkbridge` remotes. Scope reported to
     the user; git history intentionally not rewritten.
2. **`SeedDevelopmentUserAsync` and `MigrateAsync()` had no Production
   gate.** The migration guard was `if (!app.Environment.IsEnvironment("Testing"))`;
   extended to also exclude `IsProduction()`, because the deployed app
   authenticates to SQL as a managed identity scoped to
   `db_datareader`/`db_datawriter` only - it has no DDL rights, so
   `MigrateAsync()` would fail this way on every cold start. Migrations
   now run manually, under an Entra admin identity, never automatically.

Both verified with a clean `dotnet build` (0 warnings, 0 errors) before
touching Azure, and both landed as their own commit before Step B.

## Step B — deploy the API + verify MI auth ✅ (on a different App Service than Step A)

**The SQL user from Step A was confirmed created** (`quotesapi-day7-api`,
`EXTERNAL_USER`, `db_datareader` + `db_datawriter`, verified by querying
`sys.database_principals` directly rather than trusting the T-SQL script
ran without checking).

### What actually happened, in order

1. **First zip deploy failed cleanly.** `az webapp deploy ... --type zip`
   returned `DeploymentFailed` (Kudu 400). Root cause: Windows
   PowerShell's `Compress-Archive` writes backslash path separators for
   nested folders in the zip's central directory on this machine (e.g.
   `fr\Microsoft.Data.SqlClient.resources.dll`), which Kudu's Linux-side
   `rsync` can't stat as a real subdirectory. Fixed by building the zip
   with Python's `zipfile` module instead, forcing forward slashes.
2. **A corrected deploy still didn't come up - the App Service quota was
   exhausted.** `az webapp show` reported `state: QuotaExceeded,
   usageState: Exceeded` - the F1 tier's **60 CPU-minute/day hard cap**,
   flagged as a known ceiling in this README's design section, got hit
   almost immediately. At the time this looked like it could be a
   crash-loop from the MI/SQL connection failing repeatedly (a
   reasonable first guess, given the whole point of this deploy was
   proving MI auth). It was not proven to be that - it was inferred and
   later shown to be a different cause entirely (see below). The F1 app
   (`quotesapi-day7-api`, `rg-quotesapi-day7`) is quota-suspended and
   abandoned; it is **not** the app this deployment ultimately uses.
3. **Moved to a new B1 plan** (`rg-quotesapi-day17`, `quotesapi-day17-api`,
   Central India), system-assigned MI enabled, a fresh SQL contained user
   (`quotesapi-day17-api`) created on the same `quotesapi-day7-sql`
   server/`QuotesApiDay7` database with the same minimal
   `db_datareader`+`db_datawriter` scope. Before deploying anything,
   filesystem application logging was turned on and `az webapp log tail`
   started, specifically so the real startup trace would be visible
   instead of inferring a cause from a suspended app a second time.
4. **The streamed log showed the real crash cause, and it wasn't SQL at
   all**:
   ```
   Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
   DataAnnotation validation failed for 'JwtOptions' members: 'SigningKey'
   with the error: 'The SigningKey field is required.'.
   ```
   `appsettings.json` deliberately ships `"SigningKey": ""` (secret
   excluded from source by design - see `Options/JwtOptions.cs`), and
   nothing had supplied a real value on either App Service. Every
   deploy/restart crashed on host startup validation before the process
   ever got far enough to open a SQL connection - which is almost
   certainly what actually burned through the F1 quota in step 2 above,
   not an MI/SQL failure as first suspected. Fixed by generating a fresh,
   random 48-byte key (`openssl rand -base64 48`) and setting it as the
   `Jwt__SigningKey` app setting - never printed, never written to any
   file, distinct from the local dev secret in user-secrets.
5. **After that fix, the app started successfully**
   (`Now listening on: http://[::]:8080`, `Application started`), and:
   ```
   GET /api/quotes?page=1&size=5 -> HTTP 200
   {"page":1,"size":5,"total":27, ...}
   ```
6. **MI auth proven, not assumed** - queried `sys.dm_exec_sessions`
   joined to `sys.dm_exec_connections` on the live database:
   ```
   login_name   : ee79fe40-142c-4f20-852b-7c8e5c0dbd4f@77850273-4494-4bd9-9f24-7b0d5cf64e87
   program_name : EFCore/10.0.10 (Ubuntu 24.04.4 LTS X64)
   auth_scheme  : FEDERATED
   ```
   `auth_scheme: FEDERATED` is Azure SQL's marker for token-based (Entra)
   auth - never used for SQL-password logins. Cross-checked the leading
   GUID independently (`az ad sp show --id ee79fe40-...`) rather than
   assume it matched the app: it resolves to
   `displayName: quotesapi-day17-api, servicePrincipalType: ManagedIdentity`.
   No password anywhere in this path.

**Live API**: `https://quotesapi-day17-api.azurewebsites.net`

## Step C — frontend deployment, and the Static Web Apps blocker

### Static Web Apps is unreachable on this subscription - proven, not assumed

Five `az staticwebapp create` attempts, one per SWA-supported region, all
identical failures:

```
az staticwebapp create --name quotesapi-day17-web --resource-group rg-quotesapi-day17 --location "East Asia" --sku Free
```
```
ERROR: (RequestDisallowedByAzure) Resource 'quotesapi-day17-web' was disallowed by Azure: This policy
maintains a set of best available regions where your subscription can deploy resources. The objective
of this policy is to ensure that your subscription has full access to Azure services with optimal
performance. Should you need additional or different regions, contact support..
Code: RequestDisallowedByAzure
Target: quotesapi-day17-web
```

The same command, same error, for `Central US`, `East US 2`, `West US 2`,
and `West Europe` (West Europe's error additionally appended `'The
selected region is currently not accepting new customers'`).

This is not the "SWA isn't available in Central India" limitation this
README's design section already knew about - it's a second, independent
wall. Confirmed by reading the actual policy, not by re-reading the error
text:

```
az policy assignment show --name sys.regionrestriction
"listOfAllowedLocations": ["indonesiacentral", "austriaeast", "koreacentral", "centralindia", "uaenorth"]
```

Static Web Apps is only offered in five regions: Central US, East US 2,
West US 2, West Europe, East Asia. This subscription's allowlist is a
completely different five regions. The overlap is empty. There is no
region choice that makes `az staticwebapp create` succeed here - this
isn't a matter of picking better, it's not offered on this subscription
at all, in any region, full stop.

**Substitute: an Azure Storage static website**, in `centralindia` (an
allowed region), `quotesapiday17web`, `$web` container, `index.html` as
both index and 404 document. This was explicitly discussed and approved
before building it, after an earlier version of this session substituted
it without surfacing the change clearly enough first.

### Three measured costs of Storage vs. real SWA

Storage's static website hosting is not a drop-in replacement - three
concrete, measured differences, not assumptions:

1. **Parameterised routes return HTTP 404, not 200.** SWA's
   `navigationFallback` rewrites any unmatched path to `index.html` with
   a real `200`. Storage's static website only has a single, fixed
   "error document" - it serves `index.html`'s *content* for an unknown
   path, but the status code stays `404`. Verified directly:
   ```
   GET /quotes/2  -> HTTP 404, body is index.html, app renders normally
   ```
   Fixed for the five *static* routes by uploading literal, unhashed
   copies of `index.html` as blobs named exactly `search`, `quotes`,
   `collections`, `create`, `login` - each now returns a genuine `HTTP
   200`. This only works for routes with no dynamic segment; `/quotes/:id`
   has no finite set of paths to pre-create, so it still returns 404
   (with correct content) for any id, and there is no way to fix that on
   Storage without a rewrite/reverse-proxy layer in front of it.
2. **No automatic compression.** SWA compresses responses at the edge;
   Storage does not compress anything server-side. Every JS/CSS/HTML
   asset was gzip-compressed manually (Python's `gzip` module, since
   `Compress-Archive`-style tools don't produce plain gzip streams) and
   uploaded with an explicit `Content-Encoding: gzip` header set via
   `az storage blob upload-batch --content-encoding gzip`. If a future
   deploy forgets this step, assets silently stop being compressed -
   there's no platform-level fallback the way there is on SWA.
3. **HTTP/1.1 only - confirmed by Lighthouse's own `modern-http-insight`
   audit**, not inferred: every request to
   `quotesapiday17web.z29.web.core.windows.net` came back
   `"protocol": "http/1.1"`, with an estimated 200-230ms of savings
   available from HTTP/2 multiplexing (`Est savings of 200 ms` in one
   run, `230 ms` in another - normal measurement variance, same
   conclusion). Storage static websites do not support HTTP/2 or HTTP/3
   at all. Fixing this requires putting Azure CDN or Front Door in front
   of the storage account - an edge/CDN layer, which is exactly what
   today's constraints ruled out. Documented as a known, accepted ceiling
   of this architecture, not something to chase further today.

### Custom domain - not achievable today

Both SWA and Storage static websites support attaching a custom domain,
but both require a domain to attach it to. No domain is owned for this
project, so this requirement cannot be met regardless of which hosting
option is used. Not a Storage-specific limitation - would be exactly as
unmet on real SWA.

### Build-time API URL injection (never committed)

No `environment.ts`/`fileReplacements` mechanism existed before this -
every service hardcoded `http://localhost:5041` as a literal constant.
Added:
- `src/environments/environment.ts` (dev default) and
  `environment.production.ts` (ships with the literal placeholder
  `__API_BASE_URL__`, deliberately not a real-looking URL, so a skipped
  substitution step fails loudly instead of silently building against
  the wrong API).
- `fileReplacements` wired into `angular.json`'s `production`
  configuration.
- The six files that previously hardcoded the URL (`quote.service.ts`,
  `auth.service.ts`, `collections.store.ts`, `quotes.store.ts`,
  `add-quote.component.ts`, `quote-search.component.ts`) now import
  `environment.apiBaseUrl` instead. One user-visible string was
  deliberately left untouched per this project's "don't change
  user-visible strings" constraint: `quotes.store.ts`'s error message
  still literally says "Is it running on http://localhost:5041?" even in
  production - a known, accepted cosmetic rough edge, not a functional
  bug.
- The GitHub Actions workflow substitutes the real URL via `sed`
  immediately before `ng build --configuration production`; the
  committed source never contains it.

### CORS - added, not just repointed (nothing existed for Production before)

CORS was Development-only before today - `app.UseCors(...)` was never
called outside `IsDevelopment()`, meaning **any** deployed frontend would
have been blocked by the browser regardless of origin. Added a
`Cors:AllowedOrigin`-driven policy (config-supplied, no origin hardcoded
in source) registered and applied only when that setting is present and
the environment isn't Development. Set via app setting to the Storage
static website's origin, redeployed, and verified directly with `curl`:
preflight (`OPTIONS`) and an actual `GET` from the allowed origin both
return the correct `Access-Control-Allow-Origin` header; the same `GET`
with no `Origin` header, or a different one, correctly gets no such
header.

### GitHub Actions + OIDC - written, partially unverified

`.github/workflows/deploy-frontend.yml`: triggers on push to
`day17/azure-swa-deploy` scoped to the app's path, builds with the
injected URL, authenticates to Azure via `azure/login@v2` OIDC (no
stored credential of any kind), clears and re-uploads `$web`.

- Entra app registration `quotesapi-day17-github-oidc` created, with a
  federated credential trusting exactly
  `repo:DeepakMeena222187/Thinkschool:ref:refs/heads/day17/azure-swa-deploy`
  - no client secret exists for this app at all.
- GitHub repo variables set (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
  `AZURE_SUBSCRIPTION_ID`, `AZURE_STORAGE_ACCOUNT`, `API_BASE_URL`) -
  none are secrets; none are credentials by themselves without the
  federated trust.
- **Not yet confirmed**: granting the OIDC service principal `Storage
  Blob Data Contributor` (scoped to just this one storage account) has
  failed identically on every attempt so far -
  `(MissingSubscription) The request did not have a subscription or a
  valid tenant level resource provider` - including a read-only
  `az role assignment list` check, and including after a full `az login`
  re-auth that fixed an apparently-identical error for a different role
  grant earlier in this session. Until this role exists, the workflow
  will fail at the `az storage blob upload-batch` step the first time it
  runs. This is a known, open item - not silently left unmentioned.

### Manual deploy pipeline (what's actually live right now)

Everything currently served at
`https://quotesapiday17web.z29.web.core.windows.net/` was deployed
manually, following the same steps the CI workflow encodes:
1. `ng build --configuration production` with the real API URL
   substituted in (not committed - reverted to the placeholder
   immediately after each build).
2. Gzip every JS/CSS file, plus `index.html` and five unhashed copies of
   it named for each static route (`search`, `quotes`, `collections`,
   `create`, `login`).
3. Upload in four groups, each with the correct properties:
   - Hashed JS/CSS: `Content-Encoding: gzip`,
     `Cache-Control: public, max-age=31536000, immutable` (safe forever -
     the filename changes if the content does).
   - `index.html` + the five route copies: `Content-Encoding: gzip`,
     `Cache-Control: no-cache` (these are unhashed - the browser must
     always revalidate, or a stale app shell could be served
     indefinitely after a deploy).
   - `favicon.ico`: uploaded plain, untouched.
4. Verified all six routes directly: `/`, `/search`, `/quotes`,
   `/collections`, `/create`, `/login` all return `HTTP 200`;
   `/quotes/2` (dynamic id) returns `404` with correct content, as
   documented above.

**Live frontend**: `https://quotesapiday17web.z29.web.core.windows.net/`

## Lighthouse - 91 / 100 / 100 / 100 (target was ≥95 on all four)

Desktop preset, logged out (fresh headless Chrome profile, no cookies/
session - what a first-time visitor actually gets, including the auth
guard redirecting `/`, `/search`, and every other route to
`/login?returnUrl=...`).

| Category | Before fixes | After | Target |
|---|---|---|---|
| Performance | 86 | **91** | ≥95 - not met |
| Accessibility | 98 | **100** | met |
| Best Practices | 100 | **100** | met |
| SEO | 90 | **100** | met |

Fixed and confirmed via re-run:
- **SEO (90 → 100)**: added an accurate `<meta name="description">` to
  `index.html` - the single failing SEO audit (`meta-description`).
- **Accessibility (98 → 100)**: wrapped `<router-outlet>` in a `<main>`
  landmark in `app.html`, without touching `.task2-section`'s existing
  structure - fixed `landmark-one-main` without disturbing the Task 3/
  Task 5-verified topbar and layout work.
- **Cache-Control**: fixed as part of the manual deploy pipeline above
  (`cache-insight` audit was flagging ~96 KiB of assets with
  `cacheLifetimeMs: 0`).

### Performance sits at 91, honestly, not fixed further - here's why

Every metric except one is already excellent: FCP 0.9s (score 0.92), LCP
0.9s (0.96), TBT 0ms (1.0), Speed Index 0.9s (0.98), TTI 0.9s (1.0). The
entire Performance gap is **Cumulative Layout Shift** - one shift,
`section.task2-section`, score dropped from 0.63 (0.194) to 0.70 (0.171)
across two rounds of fixes:
1. Gave `.task2-section` (the shared container behind `<router-outlet>`
   for every route, not just login) a `min-height: 60vh`, reasoning it
   would stop the section from sizing to empty content while the route's
   lazy chunk loads. This helped (0.194 → 0.171) but didn't close the
   gap, because 60vh (~570px) still undershoots the settled content
   height (545px, then 660px in later runs) - the jump got smaller, not
   gone.
2. Hypothesized the real cause was the `:has(app-login .login-form)`
   CSS-gated *layout mode switch* itself (block → 2-column grid, only
   once the login form renders) - promoted `display: grid`,
   `grid-template-columns`, and `.task2-section`'s `grid-column: 1/-1`
   into `.shell`'s unconditional base rule, confirmed via cascade
   specificity math (and verified live in the deployed JS bundle) that
   this genuinely cannot affect the logged-in dashboard, since
   `:has(app-login .status-bar) { display: block }` has higher
   specificity and wins regardless. **This made no measurable
   difference** - CLS was statistically identical before and after
   (0.1711 vs 0.1713). The hypothesis was wrong; the mismatch from fix
   #1 (min-height undershooting settled height) was the entire story
   both times.

Stopped here rather than continue iterating, for a concrete reason: a
manual browser Lighthouse run attributed the dominant shift to a
**different element entirely** - `div.add-quote` at 0.220 of a 0.221
total - not `section.task2-section`. That same manual run's "unused
JavaScript" figure (990 KiB) was contaminated by installed Chrome
extensions (axe DevTools and others show up as page JavaScript to
Lighthouse when run through a normal browser profile, not our bundle).
Two runs disagreeing on which element is even responsible means the
signal is noisy enough that further blind iteration would be chasing
measurement artifacts, not a real, stable fix. The honest state:
Performance is 91, the remaining 4 points are one CLS shift whose exact
root cause is not yet pinned down with confidence, and it would need a
clean, extension-free, repeated-run methodology (not another guess-and-
measure cycle) before spending more time on it.

## Known open items

- **OIDC role assignment unresolved** (see GitHub Actions section above)
  - the frontend deploy workflow will not succeed until
    `Storage Blob Data Contributor` is confirmed granted to the
    `quotesapi-day17-github-oidc` service principal, scoped to
    `quotesapiday17web`.
- **The abandoned F1 App Service** (`quotesapi-day7-api`,
  `rg-quotesapi-day7`) is still sitting quota-suspended and undeleted.
- **Plaintext dev password's git-history scope** was reported to the
  user (29 commits, 40 files, ~20 day-folders, both `origin` and
  `thinkbridge` remotes) but deliberately not remediated - rewriting
  history was explicitly out of scope for that request.
- **CLS on the login route** (see Lighthouse section) - Performance is 4
  points short of target, root cause not fully pinned down across
  conflicting measurements.
