# Task 2 — Quote List + Detail

**Path note:** there is no `Day13/Task2-ListDetailComponent` directory. This feature was
built as a new, self-contained folder (`quote-list-detail/`) inside the existing
[Task1-SignalsZoneless/quote-search-app](../../../../..) workspace instead of a second
Angular project. That workspace already had `HttpClient` provided, zoneless change
detection on, and — critically — the API's CORS policy only allows
`http://localhost:4200`, which this workspace already runs on. Both endpoints this
feature calls are anonymous, so there was no auth wiring to duplicate by reusing the
workspace. It doesn't touch the sibling `quote-search/` feature; it's mounted
independently from `app.html` as its own `<app-quote-list-detail />` section.

## What was built

Three components plus a service, wired as list → (click) → detail:

```
quote-list-detail/
  quote.service.ts                     # HttpClient wrapper, two GET calls
  quote-list.component.ts/html/css     # left panel: fetches and lists quotes
  quote-detail.component.ts/html/css   # right panel: fetches detail for the selected id
  quote-list-detail.component.ts/html/css  # container: owns selectedId, wires the two together
```

`QuoteListDetailComponent` holds `selectedId = signal<number | null>(null)`. Clicking a
row in `QuoteListComponent` emits `(quoteSelected)`, which sets that signal; it's passed
down to `QuoteDetailComponent` as a signal input, which reacts to it (see race-condition
section below). All three signals required by spec exist independently on each side —
list has its own `loading`/`error`/`quotes`, detail has its own `loading`/`error`/`quote`
— so a failure or in-flight fetch on one side never touches the other's state.

Every component injects `QuoteService` via `inject()`, not constructor injection.

## Real endpoints and fields

Both confirmed directly against the running API (`Day5/Task6-Resilience/QuotesApi`,
`http://localhost:5041`) before writing any code — not assumed from the task description:

```
GET /api/quotes?page={page}&size={size}   → { page, size, total, items: Quote[] }
GET /api/quotes/{id}                      → Quote, or 404 ProblemDetails if id doesn't exist
```

```ts
interface Quote {
  id: number;
  author: string;
  text: string;
  createdAtUtc: string;
  ownerId: number;
}
```

This interface is **not duplicated** — it's the existing `Quote`/`QuoteListResponse` from
`../models/quote.models.ts`, already an exact match verified against
`QuotesApi/Models/Quote.cs` and `QuoteEndpointExtensions.cs`. No `any` anywhere in this
folder (`grep -rn "\bany\b" quote-list-detail/` returns zero matches).

`QuoteListComponent` makes exactly one fixed call, `getQuotes(1, 50)`, in its
constructor — there is no pagination control in the UI. That was in scope for this task
(GET .../quotes?page=1&size=50 was the literal spec'd call); it's called out here because
it also means the "empty list" state has no user-reachable trigger in the real app (see
Verification below).

## The switchMap race-condition decision

`QuoteDetailComponent.selectedId` is a signal input. The constructor converts it with
`toObservable(this.selectedId)` and pipes it through `switchMap`:

```ts
toObservable(this.selectedId)
  .pipe(
    switchMap((id) => {
      this.quote.set(null);
      this.error.set(null);
      if (id === null) { this.loading.set(false); return EMPTY; }
      this.loading.set(true);
      return this.quoteService.getQuoteById(id).pipe(
        catchError((err: HttpErrorResponse) => {
          this.loading.set(false);
          this.error.set(err.status === 404 ? `No quote exists with id ${id}.` : 'Failed to load quote detail.');
          return EMPTY;
        }),
      );
    }),
    takeUntilDestroyed(),
  )
  .subscribe((quote) => { this.loading.set(false); this.quote.set(quote); });
```

**Why `switchMap` over a manual request-id counter or `effect()`+cleanup:** every time
`selectedId` emits a new value, `switchMap` unsubscribes the previous inner
`getQuoteById(...)` observable *before* subscribing to the new one. Angular's
`HttpClient` aborts the underlying HTTP request on unsubscribe — so clicking quote A then
quote B before A resolves doesn't just discard A's late response when it arrives, it
cancels A's request outright. A manual "compare against the latest request id" counter
only achieves the discard half of that; it still lets the stale network call complete.
`switchMap` was chosen deliberately over the plain-`subscribe()` pattern already used
elsewhere in this codebase (`QuotesStore`, `CollectionsStore`) specifically because
those stores never re-fetch in response to a rapidly-changing input — this is the one
fetch in the app that does, so it's the one place that needed this instead of the
codebase's usual pattern.

**Verified**, not just argued: a Playwright script drove the real running app against
the real API with the network throttled (300ms latency, 50kbps) via CDP
`Network.emulateNetworkConditions`, clicked quote A then quote B ~50ms later, and
confirmed the detail panel settled on B. A longer alternating A→B→A→B→A→B→A sequence
under the same throttling correctly settled on the final click, A. Zero console errors
throughout.

## The verification gap that was caught

Worth recording honestly, not glossing over: the switchMap logic itself was correct on
the first write — there's no earlier naive `subscribe()`-only draft that got replaced,
and the first race-condition Playwright run already passed. The actual mistake was in
the *verification process*, not the code:

The first round of "verification" only checked 5 things — list loads, detail's initial
"nothing selected" message, click-to-detail happy path, the race condition, and absence
of console errors — and was reported as covering the feature. It did **not** exercise
the error state (a real 404) or the empty-list state at all, despite both being states
the original spec explicitly required ("Handle all three states visibly: loading, error,
empty"). Those two states were only actually tested after being asked for directly, in a
later turn:

- **404**: confirmed via `curl http://localhost:5041/api/quotes/9999` (real 404,
  `ProblemDetails` body), then driven through the real component in-browser (via
  Angular's dev-mode `window.ng.getComponent` debug API, since the UI itself only lets
  you click ids that exist) — the detail panel correctly rendered
  `"No quote exists with id 9999."`
- **Empty list**: confirmed via `curl ".../api/quotes?page=999&size=10"` (real 200,
  `items: []`), then driven the same way through `QuoteListComponent` — the panel
  correctly rendered `"No quotes found."` with no blank/broken markup

Lesson for next time: when a spec calls out N required states, verify all N before
calling the feature done, not just the happy path plus whichever failure mode is easiest
to script (network-throttled race condition, in this case) — error and empty states are
just as easy to hit with a bad-input curl and a debug-API-driven Playwright call, and
should be checked with the same rigor as the happy path, proactively, not only when
asked.

## What breaks if the API contract changes

- **Renaming/removing a `Quote` field** (`id`, `author`, `text`, `createdAtUtc`,
  `ownerId`): TypeScript won't catch this at compile time in the templates (interpolation
  of a missing property on an untyped `any` would silently print `undefined`), but since
  `Quote` is a real interface, referencing a renamed/removed field in
  `quote-detail.component.html` (`q.id`, `q.ownerId`, `q.createdAtUtc`) would fail
  Angular's template type-checking at build time (`ng build`) as long as strict template
  checking stays on — it would not fail silently.
- **Changing the list envelope shape** (e.g. renaming `items` to `data`, or dropping
  `page`/`size`/`total`): breaks `QuoteListComponent`'s `res.items` access the same way —
  a build-time template/type error, not a runtime surprise, since `QuoteListResponse` is
  a real interface.
- **Changing the 404 status code or removing the `ProblemDetails` body** on
  `GET /api/quotes/{id}`: `QuoteDetailComponent`'s `err.status === 404` check is the only
  thing driving the friendly "No quote exists with id N" message. If the API started
  returning e.g. a 200 with a null body for a missing id instead of a 404, this would
  silently render as if `quote()` were a real (but empty) object rather than showing an
  error — that failure mode was not covered by the tests above and would need a new
  contract test to catch.
- **Moving the API off `http://localhost:5041`** or serving it from an origin other than
  what the API's own CORS policy allows: every request in `quote.service.ts` hardcodes
  this base URL (matching the existing `QuotesStore`/`CollectionsStore` pattern in the
  sibling feature) — there's no environment config layer, so this would need a manual
  find-and-replace, not a config change.
- **Requiring auth on either endpoint**: both calls currently go through with no
  `Authorization` header. If either endpoint started requiring one, every request from
  this feature would start failing with 401s, surfaced as the generic "Failed to load
  quotes." / "Failed to load quote detail." messages (401 isn't special-cased the way 404
  is) rather than anything actionable.
