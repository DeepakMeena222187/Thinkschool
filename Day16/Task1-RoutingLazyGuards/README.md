# Day 16 — Routing, Lazy Loading, Guards

Workspace: `Day13/Task1-SignalsZoneless/quote-search-app` (Angular 21, signals, zoneless).
Backend: `Day5/Task6-Resilience/QuotesApi` on `http://localhost:5041`.

This workspace had **no router at all** before this task — no `app.routes.ts`,
no `provideRouter`, no `<router-outlet>`. Everything in `App` was statically
composed. Everything below was built from scratch on top of that.

## Real endpoints and the id field used

- `GET /api/quotes?page=N&size=N` → `{ page, size, total, items: [{ id, author, text, createdAtUtc, ownerId }] }`
- `GET /api/quotes/{id:int}` → `200` with the quote, or `404` ProblemDetails
  `{ type, title: "Quote not found", status: 404, detail: "No quote exists with id N." }`
- Both are anonymous on the server. Only `POST`/`DELETE` require auth.
- The `{id:int}` constraint means a non-integer id never reaches the ASP.NET
  handler — there's no 400 for `/api/quotes/abc`, the route just doesn't match.

The `id` field from the API response is a `number`; the Angular route param
for `:id` is always a `string`. The detail page parses and validates that
string itself (see "Three outcomes" below) rather than assuming it's already
a valid integer.

## What was built

### 1. Lazy routes, separate chunks

`src/app/app.routes.ts` defines three routes, each via `loadComponent`:

| Path | Component | Guard |
|---|---|---|
| `/quotes` | `QuoteListPageComponent` | `authGuard` |
| `/quotes/:id` | `QuoteDetailPageComponent` | `authGuard` |
| `/login` | `LoginPageComponent` | — |

`QuoteListPageComponent` and `QuoteDetailPageComponent` live in separate
files under `src/app/quotes-page/`, and neither statically imports the
other. Verified with `ng build`:

```
Lazy chunk files   | Names
chunk-ILRDC2GQ.js  | quote-detail-page-component
chunk-3QNJRIG3.js  | quote-list-page-component
chunk-LHPL6GZF.js  | login-page-component
```

Also confirmed live in the browser (Playwright): visiting `/quotes` loads a
different `.js` chunk than visiting `/quotes/2` directly.

**Why `loadComponent` over `loadChildren`**: there's no child-route tree here
— each path resolves to exactly one standalone component with no nested
routes of its own. `loadChildren` exists to lazy-load a whole *routing
module/config* (a feature with its own sub-routes); using it for a single
leaf component is unnecessary indirection. `loadComponent` lazy-loads the
component directly and is the idiomatic choice for standalone Angular when
a route has no children — which is the case for both `/quotes` and
`/quotes/:id`.

### 2. Auth guard

`src/app/auth/auth.guard.ts` — a `CanActivateFn` using `inject()`, no class:

```ts
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
```

**Reuses the existing auth mechanism** — `AuthService.isAuthenticated`
(`src/app/auth/auth.service.ts`), a `computed()` signal over the token
signal that's itself seeded from `localStorage`. No second source of truth
was created; the guard just reads the same signal every other part of the
app already reads.

**Where it's applied, and why at that level**: on both `/quotes` and
`/quotes/:id` individually, as flat sibling routes, each with its own
`canActivate: [authGuard]` — not nested as parent/child with the guard on
the parent only. Two reasons:
1. The routes are genuinely siblings, not a parent/list with a child/detail
   outlet — `/quotes/:id` is a full top-level page, not rendered inside
   `/quotes`'s template. Nesting them would mean restructuring
   `QuoteListPageComponent` to host a child `<router-outlet>`, which the
   task didn't ask for and which would change how the list page works.
2. Explicit per-route guards mean a direct link to `/quotes/5` (e.g. someone
   pasting a URL, or a bookmark) is protected on its own — it doesn't depend
   on ever having passed through `/quotes` first.

**On failure**: redirects to `/login` with `returnUrl` set to the full
attempted URL (`state.url`, e.g. `/quotes/5`), via `router.createUrlTree`
(not `router.navigate` — returning a `UrlTree` from a guard is the
documented way to redirect without a race between the guard's `false`
and a separate imperative navigation).

**Login honouring `returnUrl` on success**: the existing `LoginComponent`
(`src/app/login/login.component.ts`) was **not modified** — it's used
unconditionally elsewhere (always inline in the topbar) and has no route
context there. Instead, `src/app/login/login-page.component.ts` is a new,
thin wrapper — the `/login` route target — that renders the existing
`<app-login />` and adds one `effect()`: when `AuthService.isAuthenticated()`
becomes `true`, it reads `returnUrl` from the query params and navigates
there. This keeps `AuthService` as the single source of truth and the
login *form* as the single implementation — the wrapper only adds the
routing behaviour that only makes sense once a route exists to return to.

### 3. Detail component — three distinct outcomes

`QuoteDetailPageComponent` (`src/app/quotes-page/quote-detail-page.component.ts`)
reads `:id` via **component input binding** (`input.required<string>()`,
enabled by `withComponentInputBinding()` in `provideRouter`), then resolves
to exactly one of three states, each with its own message:

| Outcome | Trigger | Message | API called? |
|---|---|---|---|
| `invalid` | `id` fails `/^\d+$/` | `"abc" isn't a valid quote id.` | **No** — rejected before `QuoteService.getQuoteById` is ever called |
| `not-found` | valid int, server returns 404 | `No quote exists with id N.` (the server's own ProblemDetails `detail`, via `mapHttpErrorToAppError`) | Yes |
| `found` | valid int, 200 | renders the quote | Yes |

Verified live: `/quotes/abc` shows the invalid message with **zero** requests
to the backend (confirmed via network capture); `/quotes/999999` (a
real-but-absent id) shows the not-found message with **exactly one** request
to `/api/quotes/999999`.

### 4. View transitions

`provideRouter` is configured with `withViewTransitions()`. The list item
(`quote-list-page.component.html`) and the detail heading
(`quote-detail-page.component.html`) both bind
`[style.view-transition-name]="'quote-card-' + id"` using the same quote id,
so the browser morphs the clicked list card into the detail heading instead
of a flat cross-fade. Confirmed visually and via DOM in the Playwright run.

**Reduced motion**: handled via the `onViewTransitionCreated` callback
(`app.config.ts`) — if `matchMedia('(prefers-reduced-motion: reduce)')`
matches, `transition.skipTransition()` is called, which falls back to a
plain DOM swap with no animation. A CSS-only duration tweak for the
non-reduced case lives in `src/styles.css`, since `::view-transition-*`
pseudo-elements render on the document root and are unreachable from any
component's scoped stylesheet (a general Angular constraint, not specific
to this feature).

### 5. Reused, not reimplemented

- **Auth header + retry-on-idempotent-GET**: both interceptors
  (`auth.interceptor.ts`, `http/retry.interceptor.ts`) are already global,
  registered once in `app.config.ts`. The new pages call the existing
  `QuoteService`, which uses the app's single `HttpClient` — nothing new to
  wire up.
- **Typed error mapping**: `QuoteDetailPageComponent` calls the existing
  `mapHttpErrorToAppError` (`http/app-error.ts`) in its `catchError`, rather
  than hand-rolling status-code branching the way some pre-existing sibling
  components do. This is also why the not-found message is the server's
  actual ProblemDetails `detail` text, not a re-typed guess.
- **404 is not retried** — confirmed by reading `retry.interceptor.ts`:
  `isTransient` only returns `true` for `status === 0` or `status >= 500`;
  404 falls through untouched. Confirmed live too: the `/quotes/999999`
  network capture shows exactly **one** `GET /api/quotes/999999`, not the
  three attempts a retried request would produce.

## The guard is a UX boundary, not a security boundary

**This is explicit, not a footnote**: `authGuard` only controls what the
Angular router will navigate to inside this app. `GET /api/quotes` and
`GET /api/quotes/{id}` are anonymous on the server — confirmed directly
against the running API. Anyone can `curl http://localhost:5041/api/quotes/2`
right now with no token and get a `200`. The guard stops an unauthenticated
user from reaching the `/quotes` and `/quotes/:id` *views in the browser*;
it does nothing to the data itself, which was never protected in the first
place. If this needed to be a real security boundary, that protection would
have to be added server-side (e.g. `RequireAuthorization` on those two GET
endpoints), which is a backend change outside this task's scope and not
something the client can enforce on its own.

## Double-render fix

`LoginPageComponent` renders `<app-login />`, and the app's topbar
(`app.html`) also renders `<app-login />`. Originally the topbar instance
was unconditional, so `/login` briefly showed the login form twice. Fixed
by making the topbar instance route-aware:

`App` (`app.ts`) now derives `isLoginRoute` — a `computed()` signal over the
router's current URL (`toSignal(router.events.pipe(filter(NavigationEnd), ...))`,
comparing the path against `/login`) — and `app.html` wraps the topbar's
`<app-login />` in `@if (!isLoginRoute())`.

**Why this over the alternative** (`LoginPageComponent` owning the form
directly instead of reusing `LoginComponent`): that would mean
re-implementing `LoginComponent`'s fields, submit handling, and mode-toggle
logic a second time — exactly the "second source of truth" this task's
original spec said to avoid. Hiding the topbar instance keeps exactly one
real `LoginComponent` implementation, reused via routing, with no
duplicated logic. `LoginComponent` itself was not touched — not its fields,
its submit handling, or any of its strings; `isLoginRoute` only decides
*which one of the two mount points* renders it.

The `:has(app-login .login-form)` / `:has(app-login .status-bar)` selectors
in `app.css` that switch the whole page between hero-split and dashboard
layout are unaffected — they match *any* `app-login` inside `.shell`, and
the routed instance alone is enough to keep them working correctly on
`/login`.

Re-verified live after the fix: exactly one `<app-login>` renders on
`/login` (was two), the guard redirect and `returnUrl` round-trip still
work end-to-end, and the topbar's login widget reappears normally on every
other route.

## Files created vs modified

**Created:**
- `src/app/app.routes.ts`
- `src/app/auth/auth.guard.ts`
- `src/app/quotes-page/quote-list-page.component.ts` / `.html` / `.css`
- `src/app/quotes-page/quote-detail-page.component.ts` / `.html` / `.css`
- `src/app/login/login-page.component.ts` / `.html` / `.css`
- `Day16/Task1-RoutingLazyGuards/README.md` (this file)

**Modified:**
- `src/app/app.config.ts` — added `provideRouter(routes, withComponentInputBinding(), withViewTransitions({...}))`. Interceptor registration (`provideHttpClient(withInterceptors([authInterceptor, retryInterceptor]))`) is untouched — same array, same order, same comment.
- `src/app/app.ts` — added `RouterLink`, `RouterOutlet` to the `imports` array; added an `isLoginRoute` computed signal (see "Double-render fix" above) used to conditionally show the topbar's `<app-login />`. No existing import removed, no existing logic changed.
- `src/app/app.html` — added one new `<section class="task2-section">` (a nav link to `/quotes` plus `<router-outlet />`), inserted between the existing "Task 2" section and the existing "Create Quote" section; wrapped the topbar's `<app-login />` in `@if (!isLoginRoute())`. No existing section's markup or text was touched.
- `src/app/app.css` — added `.day16-nav` / `.day16-nav__link` rules only, appended before the `/* --- Animations --- */` block. No existing rule was changed.
- `src/styles.css` — added a `prefers-reduced-motion: no-preference` block styling `::view-transition-group(*)`. No existing rule was changed. (Note: this file had an unrelated uncommitted local change already present before this task started — untouched, only appended to.)

## Existing behaviour changed

**None.** No existing component's user-visible strings changed, no
interceptor logic changed, no existing route (none existed) changed. Every
edit to an existing file was strictly additive (new imports, new CSS rules,
new markup inserted between existing sections). The one notable *consequence*
of an addition — the duplicated login form on `/login` — is called out above
rather than silently shipped.

## Verified in a real browser

Ran the Angular dev server against the live backend on `:5041` and drove it
with Playwright (no mock backend, no test framework changes to the repo —
`playwright` was installed with `--no-save` for this verification only and
is not a project dependency). All checks passed:

- Unauthenticated `/quotes` → redirected to `/login?returnUrl=%2Fquotes`.
- Signing up/logging in from that page → landed back on `/quotes`
  automatically (returnUrl honoured).
- `/quotes` rendered 27 real quotes from the live API.
- Clicking a quote navigated to `/quotes/{id}` and rendered its detail,
  loading a different chunk file than the one loaded for `/quotes`.
- `/quotes/999999` → not-found message, one real API call.
- `/quotes/abc` → distinct invalid-id message, zero API calls.
- No console errors or uncaught exceptions at any point (the one console
  line observed was Chromium's own network-log entry for the intentionally
  triggered 404, not an application error).
