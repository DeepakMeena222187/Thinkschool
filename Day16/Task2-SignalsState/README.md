# Day 16 Task 2 — Signals-First State Management

Workspace: `Day13/Task1-SignalsZoneless/quote-search-app` (Angular 21, zoneless).
API: `Day5/Task6-Resilience/QuotesApi` on `http://localhost:5041`.

## What was built

Not a new store from scratch — a **consolidation**. A read-only inventory
done before writing any code found `GET /api/quotes` independently fetched
into three separate, uncoordinated copies of local signal state
(`QuotesStore`, `QuoteListComponent`, and the routed `QuoteListPageComponent`
built earlier the same session), all three initializing with
`signal<Quote[]>([])` — unable to tell "haven't fetched yet" apart from
"fetched and got zero results."

`QuotesStore` was extended in place (same class, same `providedIn: 'root'`
token) rather than adding a fourth parallel implementation. The routed list
and detail pages, plus the dashboard (`QuoteSearchComponent`) and
`CollectionsComponent`, all now read from it. The older Day-13
`quote-list-detail/*` Task-2-section components were deliberately left
untouched — different composition pattern, out of scope.

## A rejected design, kept here on purpose

The first version of this store fetched everything once (`page=1, size=50`,
safely larger than the real 27-row dataset) and turned "pagination" into
client-side `Array.slice()` over that cached array, with `page`/`size` as
per-view signals slicing over one shared canonical list.

That was **rejected**, correctly: it quietly deleted real server-side
pagination from the app. `GET /api/quotes?page=N&size=N` would only ever
have been called once, with `total` becoming decorative, and
`/quotes?page=999` would have proven `Array.slice()` returns nothing, not
that a real empty API response is handled. It only worked because this
dataset has 27 rows — at real scale it would fetch a subset, slice locally,
and quietly show wrong data with no error, which is exactly the kind of
failure that doesn't announce itself. Recording this here because getting a
plausible-looking design rejected for the right reason is as much a part of
this exercise as the state management itself.

## The actual bug, and the actual fix

Before either design above, the *first* working version had a real
regression: `page`/`size` lived as a single pair of signals **on the
store**, shared by every consumer. Navigating the routed list to
`/quotes?page=999` moved that one shared cursor, which also emptied the
completely unrelated dashboard (`QuoteSearchComponent`), since it read from
the same store-level `page`/`size`. That's a real coupling bug, not an
acceptable side effect of "one store" — a store owning canonical data is
supposed to prevent that, not cause it.

**Fix**: the store owns canonical quote data — as a **keyed cache**, one
entry per `(page, size)` actually requested, real server fetches for every
key. `page`/`size` are not store state at all anymore; each view (dashboard,
routed list, collections' join) owns its own `page`/`size` signals locally
and reads only its own key. Two views can never affect each other because
they're reading and writing different map entries, not sharing one cursor.

## Real endpoints and fields used

- `GET /api/quotes?page=N&size=N` → `{ page, size, total, items:[{ id, author, text, createdAtUtc, ownerId }] }` — genuinely re-issued per distinct `(page,size)` a view asks for, not simulated.
- `GET /api/quotes/{id:int}` → `200` quote or `404` ProblemDetails (`detail: "No quote exists with id N."`) — drives `detailQuote`/`detailStatus`, unchanged by any of the above (id-keyed by construction, never part of the bug).
- Real ids 2–30, gaps at 13 and 16 (27 rows total) — used below to prove page-1 and page-2 hold disjoint real content, not just different labels.

## The store (`quotes/quotes.store.ts`)

```ts
private readonly resultsByKey = signal<Map<string, QuoteListEntry>>(new Map());
private readonly statusByKey = signal<Map<string, EntryStatus>>(new Map());
private readonly errorByKey = signal<Map<string, string>>(new Map());
```

Keyed by `` `${page}:${size}` ``. **Not a cache in the "skip a fetch" sense**
— named `resultsByKey`, not `cache`, specifically to avoid implying a hit
path that doesn't exist. `loadPage()` always issues a real request; the
keying exists purely to isolate different views' data from each other, not
to avoid re-fetching.

**Concurrency (requirement 3), corrected**:

```ts
this.loadRequests.pipe(
  groupBy(({ page, size }) => keyOf(page, size)),
  mergeMap((group$) => group$.pipe(
    switchMap(({ page, size }) => this.http.get<QuoteListResponse>(QUOTES_URL, { params: { page, size } })
      .pipe(map(res => ({ key: keyOf(page,size), ok: true, res })), catchError(err => of({ key: keyOf(page,size), ok: false, ...})))),
  )),
).subscribe((result) => { /* write into resultsByKey/statusByKey/errorByKey at result.key */ });
```

`groupBy` splits the request stream by key *before* `switchMap` ever sees
it. `switchMap` inside a group only cancels a stale request for **that same
key** — a slow page-1 request in flight cannot cancel or be overwritten by
a concurrent page-2 request; they're different groups writing to different
map entries. This is the actual mechanism that makes the fix real — an
earlier version used a single global `switchMap`, which would have
cancelled unrelated in-flight requests too, not just stale same-key ones.

**Per-view derivation** (`deriveListView`) is a pure function, not a
service — no fetch inside it, just `computed()` signals reading one specific
key reactively:

```ts
export function deriveListView(store: QuotesStore, page: Signal<number>, size: Signal<number>) {
  const items = computed(() => store.entry(page(), size())?.items ?? []);
  const status = computed<QuoteListStatus>(() => {
    const s = store.statusOf(page(), size());
    if (s !== 'loaded') return s;
    return items().length === 0 ? 'empty' : 'loaded'; // empty is still per-slice, not per-fetch
  });
  // total, error similarly
}
```

`QuoteListPageComponent` (page from `?page=`, size fixed at 50) and
`QuoteSearchComponent`/`CollectionsComponent` (both fixed at `page=1,
size=50` — genuinely independent instances, not a shared reference)
each call this once with their own signals. The dashboard and
`CollectionsComponent` happening to choose the same `(1,50)` key is
deliberate and safe — they both want "everything," so they naturally share
that cache entry — which is different from the earlier bug, where sharing
happened because there was only *one* cursor to begin with, not because two
views chose the same value on purpose.

## Verification

All against the live app + live API. Evidence in `evidence/` — screenshots
plus `verify-log-2.txt` (raw assertion data) and `verify-log.txt` (the
original loading/error checks from the first design pass, still valid since
loading/error handling wasn't part of what changed).

| # | Scenario | How | Result |
|---|---|---|---|
| A | Dashboard unaffected | Read the dashboard's own `quotes()` ids directly off the live component before and after navigating the routed list to `?page=999` | **Identical id list, byte-for-byte**, both readings: `[2,3,4,5,6,7,8,9,10,11,12,14,15,17,18,19,20,21,22,23,24,25,26,27,28,29,30]` |
| A2 | Routed list shows the empty state | Same navigation | `"No quotes found."` |
| B | Genuine server empty response, not a UI illusion | Captured the raw HTTP response for `GET /api/quotes?page=999` | `{"status":200,"body":{"page":999,"size":50,"total":27,"items":[]}}` — `total: 27` is real and non-zero; `items` is empty because page 999 is past the end, exactly what a real server should return |
| C | `entry(1,10)` / `entry(2,10)` hold disjoint real ids | Called `loadPage(1,10)` and `loadPage(2,10)` on the live store, read both keys' actual item ids | `page1=[2,3,4,5,6,7,8,9,10,11]`, `page2=[12,14,15,17,18,19,20,21,22,23]` — disjoint, and neither list contains the known gap ids 13 or 16 |
| D | Same-key race | `loadPage(5,10)` fired twice ~100ms apart, first response delayed 1.5s past the second | Settles to `status: 'loaded'` with valid items, never stuck or corrupted |

**Test D's limit, stated plainly, not implied**: because both requests hit
the real API with **identical** parameters, their responses are
byte-for-byte identical — there is no way to tell "which response's data
won" by content, unlike test C. Test D only proves the store doesn't end up
corrupted or stuck in `loading` when two requests for the same key race and
resolve out of order. It is **not** evidence that the newer response's data
specifically won over the older one — that claim would require
distinguishable responses, which the real (non-mocked) API can't produce for
identical requests. Listed separately from test C for exactly this reason;
it doesn't prove the same thing.

## What I did NOT verify myself

- Same as before: the error-state test simulates the backend being
  unreachable via Playwright's `route.abort()`, not by killing the real
  `QuotesApi.exe` process (shared with other work in this session).
- Test D's inherent limitation is above, not repeated here.
- Did not test three or more concurrent distinct-key requests in flight at
  once (only ever tested two at a time) — `groupBy`/`mergeMap` should
  generalize without change, but I haven't specifically driven three.
- Did not test what happens if the same key is requested by two different
  *components* simultaneously (e.g. dashboard and `CollectionsComponent`
  both mounting and calling `loadPage(1,50)` within the same tick) beyond
  what's implied by `switchMap`'s same-key cancellation — architecturally
  it's the same mechanism as test D, but not independently re-verified with
  two real component instances racing rather than one component calling
  `loadPage` twice.
- `addQuote()`/`removeQuote()` now write into every cached key whose page
  contains (or, for add, starts with `1:`) the affected quote — I did not
  write an automated test for this; it compiles and matches the existing
  create-quote flow's expectations, but wasn't driven through a real
  create/delete against the live API as part of this verification pass.

## Constraints check

No existing user-visible string changed. The exact `"Cannot reach the
quotes API..."` status-0 message is still preserved (unchanged from before
this redesign — the special-case in the store's `catchError` was untouched
by the keyed-cache rework). No interceptor duplicated or modified.

---

# NgRx / signal-store threshold — DRAFT ONLY

**Still a draft for you to rewrite in your own words — the exercise is
explicit that this judgment call has to be yours.**

This session's own back-and-forth is itself evidence for where the line
sits, worth naming directly rather than leaving abstract: a *plain*
per-view `deriveListView()` function plus a keyed `Map` on a single service
was enough to solve real cross-view isolation correctly, once designed
correctly — no framework needed. What made it hard wasn't lack of
tooling, it was getting the correct mental model (canonical data vs.
per-view cursor) before writing code. That's a point *in favor* of staying
signals-native longer than instinct suggests: reach for NgRx when the
*coordination* problem itself gets harder, not just when state is shared.

Concrete triggers:

1. **A view legitimately needs to merge/react across more than one keyed
   slice at once** — e.g. "show items present in page 2 of the search
   results AND not yet in any collection." A single `deriveListView` call
   reads one key; cross-key composition starts pushing toward either many
   composed `computed()`s (workable for two or three, unwieldy beyond that)
   or a normalized entity store with real selectors — which is most of what
   NgRx's entity adapter exists for.
2. **Side effects that must chain across features in a defined order**
   (delete a quote → remove from every collection → audit log → cache
   invalidate). `removeQuote()` mutating `resultsByKey` directly, as
   written here, is fine for one store reacting to one action. Three or
   more coordinated stores per mutation is the point a hand-rolled chain of
   method calls stops being reviewable, and NgRx effects (or a
   signal-store's `rxMethod` composition) earn their ceremony.
3. **Time-travel / devtools becomes a real, not speculative, need** — a bug
   report needs the *sequence* of state transitions, not just current
   state. Nothing here has needed that yet.
4. **Team size crosses a point where the convention needs enforcing, not
   documenting** — with one or two people, a comment explaining "why keyed,
   why not a cache" (as in this file) is enough; NgRx's stricter shape
   partly exists to make the right way the only easy way once more people
   who didn't write the original store are touching it.

**What I'd push back on in my own draft**: trigger 1 is the one I'd bet on
actually happening first in this specific app, given collections already
need to join against quotes. 3 and 4 are real but more speculative for a
project this size right now.
