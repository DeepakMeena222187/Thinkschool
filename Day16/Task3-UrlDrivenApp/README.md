# Day 16 Task 3 — URL-Driven App Structure

Workspace: `Day13/Task1-SignalsZoneless/quote-search-app` (Angular 21, zoneless).

## What changed

Before this task, `app.html` rendered the entire Day-13 dashboard
permanently — `QuoteSearchComponent`, `CollectionsComponent` (nested inside
it), the old `quote-list-detail/*` Task-2 section, and both create-quote
forms — all as static children of `App`, outside `<router-outlet>`. They
appeared on every route and none of their internal navigation touched the
URL. Only the two Day-16 routes (`/quotes`, `/quotes/:id`) were actually
routed.

`app.html` now contains only the skip-link, topbar (brand + nav + login
widget), the decorative hero panel, and a single `<section class="task2-section"><router-outlet /></section>`.
Every feature lives behind a route.

## Routes

| Path | Component | Guarded? |
|---|---|---|
| `/` | — (redirect) | n/a |
| `/search` | `QuoteSearchComponent` | yes |
| `/collections` | `CollectionsComponent` | yes |
| `/create` | `CreateQuotePageComponent` (new) | yes |
| `/quotes` | `QuoteListPageComponent` | yes (existing) |
| `/quotes/:id` | `QuoteDetailPageComponent` | yes (existing) |
| `/login` | `LoginPageComponent` | no |

**`/` redirects to `/search`, not `/quotes`.** `QuoteSearchComponent` was
the app's original default view before any routing existed — redirecting
root to the newer `/quotes` route instead would have changed what a
first-time visitor sees for no reason better than "it's newer."

**Guarding, reasoned per route, not blanket:**
- `/create` — the strongest case: `POST /api/quotes` has server-side
  `RequireAuthorization`. Unguarded, a logged-out user could fill the whole
  form and only discover it fails on submit.
- `/search`, `/collections` — weaker, UX-consistency reasoning: their GETs
  are anonymous, but `app.css` already had a rule
  (`.shell:has(app-login .login-form) app-quote-search { display: none; }`)
  hiding the dashboard whenever logged out, before any of this routing
  existed. Guarding means that CSS rule is no longer needed at all — the
  component can't mount while logged out, so it was deleted rather than
  kept as now-redundant defensive styling (see below).
- `/quotes`, `/quotes/:id` — unchanged from Day 16 Task 1.
- `/login` — never guarded.
- `/` needs no guard of its own; it's a pure `redirectTo`, and `/search`'s
  own guard applies to the redirected-to route.

## Components moved / deleted / new

**Moved as-is** (no internal changes): `QuoteSearchComponent` → `/search`,
`CollectionsComponent` → `/collections`.

**`CollectionsComponent` is reachable two ways, deliberately left that way.**
It isn't only a standalone route now — it's still rendered inside
`QuoteSearchComponent`'s own tab-switcher (`quote-search.component.html`'s
`@if (activeTab()==='quotes') {...} @else { <app-collections /> }`). Fixing
that redundancy would mean rewiring `setTab()`/`activeTab` to navigate
instead of toggle internal state — an explicit instruction was to not touch
`QuoteSearchComponent`'s tab-switcher, so the redundancy ships accepted,
not accidentally missed.

**Deleted as redundant**: `quote-list-detail/quote-list.component.*`,
`quote-detail.component.*`, `quote-list-detail.component.*`, and that
folder's `README.md`. These duplicated exactly what the routed `/quotes` +
`/quotes/:id` already do (own local `loading`/`error`/`quote(s)` signals,
independent fetch, parent-owned `selectedId`) — the same duplication Day 16
Task 2's inventory flagged as the actual problem there. **Kept**
`quote-list-detail/quote.service.ts` + its spec — still used by
`CreateQuoteComponent`/`CreateQuoteSignalComponent` for `createQuote()`.

**New: `create-quote-page/create-quote-page.component.ts`.** `App`'s
template used to bind `(quoteCreated)="onQuoteCreated($event)"` directly on
both create-quote forms; once they're behind a route, `App`'s template no
longer contains them, so that binding had nowhere to attach. This wrapper
renders both forms exactly as they appeared in `app.html` (same two
headings, same adjacent placement for the Day-13-vs-Day-14 comparison) and
owns the `onQuoteCreated` handler, moved verbatim from `app.ts`. Neither
`CreateQuoteComponent` nor `CreateQuoteSignalComponent` was touched.

## Things that would have broken silently, and how each was handled

- **The skip-link.** `<a href="#create-quote-heading">` jumped to a
  same-page anchor. Once Create Quote lives on a different route, that
  anchor doesn't exist on most pages. Changed to
  `<a routerLink="/create">` — same visible text ("Skip to Create Quote
  form"), navigation instead of an anchor jump. A route navigation doesn't
  natively move focus the way a same-page anchor jump does, so
  `CreateQuotePageComponent` explicitly focuses its heading
  (`viewChild` + `ngAfterViewInit`) on arrival — without that, the link
  would still change the URL but stop doing the one thing a skip link
  exists for. **Verified this specifically, not just asserted it**: axe was
  run against `/create` (0 violations, `wcag2a`/`wcag2aa`), but axe's static
  analysis does not check whether focus actually moves after a navigation —
  that needed a separate, explicit check (`document.activeElement` read
  right after activating the skip link via keyboard). Both are recorded
  in `evidence/`, not just the axe result alone.
- **`.task2-heading` CSS stopped applying.** It lived in `app.css`, scoped
  to `App`'s own template by Angular's style encapsulation. Once the
  headings using it moved into `CreateQuotePageComponent`'s template, the
  rule no longer reached them — copied verbatim into
  `create-quote-page.component.css` rather than left silently broken or
  promoted to a global stylesheet it didn't need to be in.
- **Inter-section spacing.** The two create-quote forms used to be separate
  `<section class="task2-section">` elements, each contributing its own
  bottom padding as the visual gap between them. Merged under one outer
  wrapper now, so that gap was recreated locally
  (`.create-quote-page__section + .create-quote-page__section { margin-top: 3rem; }`).
- **Now-dead CSS removed, not left behind**: `app-quote-search`'s two
  mode-specific rules (`display:none` when logged out, sizing/animation
  when logged in) — both superseded by the guard (can't mount logged out)
  and the single shared `.task2-section` wrapper (handles sizing for every
  route now, not just this one). The unused `dashboardFadeIn` keyframe that
  only that rule referenced was removed too. The old `.day16-nav`/
  `.day16-nav__link` demo-specific nav styles were replaced by general
  `.main-nav`/`.main-nav__link` rules used by the real topbar nav.
- **`App.onQuoteCreated()` and its `QuotesStore` injection became dead
  code** once the handler moved to the new wrapper — removed from `app.ts`,
  along with the now-unused static imports of `QuoteSearchComponent`,
  `CreateQuoteComponent`, `CreateQuoteSignalComponent`, and
  `QuoteListDetailComponent`. This last part turned out to be load-bearing
  for the chunk-separation requirement (below) — leaving even one of those
  static imports in place would have pulled that component back into the
  eager bundle regardless of also being reachable via `loadComponent`.
- **Eager-load-at-bootstrap timing changed.** Task 2 made "quotes loaded at
  app start" an emergent property of `QuoteSearchComponent` being
  permanently mounted. Nothing is permanently mounted now, so quotes and
  collections only fetch when their route is actually visited — a natural,
  correct consequence of "everything lives behind a route," not a bug, but
  a real behavior change from before.
- **Checked, not assumed**: whether `CollectionsComponent`'s own CSS
  implicitly depended on being nested inside `QuoteSearchComponent`'s
  `.quote-search` wrapper for correct layout. It doesn't — verified by
  screenshot with `/collections` rendered fully standalone
  (`evidence/3-collections-standalone.png`); layout is intact.

## Verification

All against the live app + live API. Evidence in `evidence/`.

**Chunk separation** — `ng build` lists all six routed components as named
lazy chunks (`quote-list-page-component`, `quote-detail-page-component`,
`quote-search-component`, `collections-component`, `create-quote-page-component`,
`login-page-component`).

**Confirmed they're not *also* eagerly bundled — the rigorous way, not the
naive one.** A naive `grep main.js` for each class name finds all six —
but that's expected and correct, not a leak: `main.js` has to contain the
literal route table, including `.then(e => e.QuoteListPageComponent)`
property-access text, or the router couldn't know what to grab off the
lazy chunk once it loads. Confirmed the actual leak-check by grepping for
distinctive strings that could only exist if each component's real
template/logic were duplicated into `main.js` (e.g. `"Filter by author"`
for `QuoteSearchComponent`, `"isn't a valid quote id"` for
`QuoteDetailPageComponent`, `"Delete the collection"` for
`CollectionsComponent`) — **zero matches for any of them.** Route-table
text in `main.js`: expected. Actual component bodies in `main.js`: absent.

**`main.js` size, before and after, both saved**: `121,788 bytes` →
`8,758 bytes` — a 113,030-byte (92.8%) reduction, confirming the static
imports removed from `app.ts` really were what had been pulling
`QuoteSearchComponent`/`CreateQuoteComponent`/`CreateQuoteSignalComponent`/
`QuoteListDetailComponent` into the eager bundle. Both files kept in
`evidence/` (`main-BEFORE.js` and the current build's `main-*.js`), not
just the byte counts.

**`/collections` standalone** — `evidence/3-collections-standalone.png`.
Renders correctly with no layout regression from losing the
`QuoteSearchComponent` wrapper context.

**Every nav link** — clicked each of the four topbar links
(`Search`/`Collections`/`Create`/`Quotes (Day 16)`) from a single running
session; confirmed both the URL changed to the expected path *and* the
route-specific component actually mounted (`app-quote-search`,
`app-collections`, `app-create-quote-page`, `app-quote-list-page` each
checked for presence, not just the URL) — all four passed.

**`/create` accessibility** — 0 `wcag2a`/`wcag2aa` violations via
`axe-core` (`evidence/axe-create-route.json`), **and**, separately, real
focus verification: tabbed to the skip link, activated it via keyboard,
confirmed the URL became `/create` and `document.activeElement` was
literally the `#create-quote-heading` element — not inferred from the axe
pass, which doesn't test this at all.

## What I did not verify

- Did not test the `/search`↔`/collections` dual-reachability path for any
  interaction bugs (e.g. state consistency between the tab-nested
  `CollectionsComponent` instance and the standalone-route instance) beyond
  confirming both render — they're separate component instances reading
  the same underlying stores, which should be safe, but I didn't
  specifically drive both at once in the same session.
- Did not re-verify the Task 1/Task 2 guard-redirect and concurrency
  behaviors from scratch here — only checked that the guard is still wired
  correctly and applied to the newly-guarded routes; the underlying guard
  and store mechanisms are unchanged from those tasks.
- `npm install playwright axe-core --no-save` was used for this
  verification only, not added to the project; note for future sessions:
  a plain `--no-save` install can prune packages not listed in
  `package.json` (it pruned Playwright once mid-session when `axe-core`
  was installed alone) — install everything needed in one command.
