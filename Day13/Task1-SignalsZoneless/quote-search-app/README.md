# Quote Search App

An Angular 21, zoneless (`provideZonelessChangeDetection()`), standalone-only (no
`NgModule` anywhere) app built against the real Week-1 API,
[`Day5/Task6-Resilience/QuotesApi`](../../../Day5/Task6-Resilience/QuotesApi)
(`http://localhost:5041`).

## This workspace covers more than one Day/Task submission — intentionally

This single Angular project is the home for several separate Day/Task exercises, not
just "Day 13 Task 1." That's deliberate, not scope creep: every exercise here needs the
same real API, the same `HttpClient` setup, the same JWT auth, and — critically — the
API's CORS policy only allows `http://localhost:4200`, which this workspace runs on by
default. Standing up a fresh Angular project per exercise would mean re-wiring
`HttpClient`, auth, and CORS-compatible config from scratch each time for no functional
benefit. So later exercises (the Task 2 list+detail feature, the Day 14 forms work) were
added as new, self-contained folders inside this same `src/app/`, each documented in its
own README, rather than spawning parallel workspaces.

## What's in `src/app/`

| Folder | What it is |
|---|---|
| [`auth/`](src/app/auth/auth.service.ts) | `AuthService` — JWT login/register, token kept in a signal + localStorage, decodes `sub` from the JWT client-side for UI display only (the server independently re-verifies and enforces ownership on every request) |
| [`login/`](src/app/login/login.component.ts) | Login/signup form; toggles between the two modes, shows a logged-in status bar with log-out once authenticated |
| [`quotes/`](src/app/quotes/quotes.store.ts) | `QuotesStore` — the shared, in-memory quote list (`GET /api/quotes?page=1&size=50`) that `quote-search` reads from and `create-quote`/`create-quote-signal` append into |
| [`quote-search/`](src/app/quote-search/quote-search.component.ts) | The original Day 13 Task 1 feature: search-by-author, quotes grouped into an accordion by author, ownership-based delete, add-to-collection |
| [`collections/`](src/app/collections/collections.component.ts) | Named collections of quotes — create a collection, add/remove quotes from it, ownership-based delete |
| [`add-quote/`](src/app/add-quote/add-quote.component.ts) | The original Day 13 Task 1 add-quote form (predates the Day 14 `create-quote`/`create-quote-signal` forms below; kept as-is, not superseded) |
| [`quote-list-detail/`](src/app/quote-list-detail/README.md) | **Day 13 Task 2.** A separate list+detail pair against the same API: click a quote in the list, its detail loads on the right. The one feature here with a fetch that changes in response to rapid input (clicking quote after quote), solved with `toObservable` + `switchMap` so a stale in-flight detail request is actually cancelled, not just outrun. |
| [`create-quote/`](src/app/create-quote/README.md) | **Day 14.** Reactive-forms (`FormGroup`/`FormControl`) create-quote form, validated to match the server's real `CreateQuoteRequest` limits exactly, full `aria-invalid`/`aria-describedby`/focus-management wiring. Also where the accessibility pass happened that found and fixed 21 critical axe violations on the unrelated add-to-collection `<select>` dropdowns in `quote-search/`. |
| [`create-quote-signal/`](src/app/create-quote-signal/README.md) | **Day 14 Task 2.** The same create-quote form rebuilt on Angular's newer `@angular/forms/signals` API (`FieldTree`/`form()`/`validate()`), side by side with the reactive-forms version for comparison rather than replacing it. |
| [`models/`](src/app/models/quote.models.ts) | Every DTO shared across the features above (`Quote`, `QuoteListResponse`, `LoginRequest`/`Response`, `CreateQuoteRequest`, `Collection`, etc.) — kept in one place instead of duplicated per feature, and each shape confirmed against the real API/DTOs, not assumed |

Every feature folder above that has its own README is linked to it — read that folder's
README first for the details on how that specific feature works, what was verified, and
why it made the decisions it did. This file is the map, not the manual.

## Accessibility

A skip-link (`app.html`, top of `.shell`) jumps keyboard/screen-reader users straight to
the create-quote form, bypassing the quote list and list+detail panels above it. Full
detail — including the 21-critical-violation axe DevTools finding and fix on the
add-to-collection dropdowns — is in
[`create-quote/README.md`](src/app/create-quote/README.md#the-axe-devtools-verification).

## Running it

Requires [`Day5/Task6-Resilience/QuotesApi`](../../../Day5/Task6-Resilience/QuotesApi)
running on `http://localhost:5041` (`dotnet run` from that folder) — every feature in
this workspace talks to it directly, there's no mock/stub mode.

```bash
npm install
ng serve
```

Then open `http://localhost:4200/`. The CORS policy on the API side (`Program.cs`,
`AddCors` → `WithOrigins("http://localhost:4200")`) is why this has to be the exact
origin — a different port will fail every request with a CORS error, not a helpful one.

## Building / testing

```bash
ng build                              # production build
ng build --configuration development  # dev build (faster, unminified — useful while iterating)
ng test                               # Vitest unit tests
```

No end-to-end test runner is configured (`ng e2e` is a no-op scaffold prompt by default);
the verification approach used across this workspace's features has instead been
Playwright driven ad hoc against the real running app and the real API — see each
feature's own README for what was actually exercised that way.
