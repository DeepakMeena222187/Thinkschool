# Day 16 Task 5 — Topbar Responsive Fix

Workspace: `Day13/Task1-SignalsZoneless/quote-search-app`. Scope: `.topbar`
layout only, in `app.css` (plus one unavoidable exception, see below) - no
Pass 2 palette, typography, or animation work touched.

## Diagnosis (measured before any fix)

Screenshotted and measured the topbar at 1440/1280/1024/900/768/600/375px,
logged in, on `/quotes`. Raw data: `evidence/diagnosis-raw.json`,
screenshots `evidence/diag-*.png`.

`document.scrollWidth` never exceeded `clientWidth` at any width - which
sounds clean but isn't: `.shell` has `overflow-x: hidden`, so anything that
overflows gets silently clipped instead of producing a scrollbar. That
metric alone would have hidden the real bug.

| Width | Finding |
|---|---|
| 1440 / 1280 / 1024 / 900 | Fine - brand, nav (one row, 440px), login sit side by side with room to spare. |
| 768 | `.main-nav`'s own internal `flex-wrap` engages *inside* its squeezed column: 3 links on one row, 1 forced to a second row, while nav stays sandwiched between brand and login on the same outer row. Topbar grows 56px → 99px tall. |
| 600 | Same squeeze, worse - nav splits into a 2+2 grid, still trapped in the middle column. |
| 375 | Nav collapses to 4 stacked rows (182px tall). The login/logout widget gets pushed to a bounding box of `left:247, right:402` against a 375px viewport - clipped by `.shell`'s `overflow-x:hidden`. Confirmed directly in the screenshot: "Log out" rendered as "Lo…ou…", not just cramped - a real, functional loss of a control, not a cosmetic squeeze. |

Root cause: `.topbar` never wrapped as a *whole* - only `.main-nav`'s own
internal wrap did. At narrower widths, nav (and eventually login) had
nowhere to go but squeeze horizontally, and past a threshold that squeeze
clipped content outright.

Also measured, not part of the "breaks at narrow widths" framing but
relevant to the explicit 44px requirement: nav link touch targets measured
36px tall at every width tested, including the wide ones. Left unchanged
at wide/medium tiers per the task's explicit "at narrow widths" scope -
only enforced at ≤480px.

## Fix

Three tiers, in `app.css`:

- **Wide (default, >860px)**: unchanged. Already verified correct at
  900/1024/1280/1440px - no reason to touch what wasn't broken.
- **Medium (≤860px)**: `.main-nav { order: 3; flex-basis: 100%; justify-content: center; }`
  under `@media (max-width: 860px)`. Forces nav onto its own full-width row
  below brand+login instead of squeezing into the middle column. 860px was
  chosen with deliberate margin between the measured "still fine" width
  (900px) and the measured "already broken" width (768px), rather than
  cutting the breakpoint exactly at either boundary.
- **Narrow (≤480px)**: `.topbar { flex-direction: column; }` with explicit
  `order` on all three children (brand → nav → login) for a clean top-to-
  bottom stack, plus `app-login { display: block; width: 100%; }` -
  `<app-login>` has no `:host` display rule (confirmed by reading
  `login.component.ts`/`.css` before relying on it), so it defaults to
  `display: inline` and would have ignored `width: 100%` without the
  explicit `display: block` override. Nav keeps its own internal
  `flex-wrap`, but now wraps within the *full* viewport width instead of a
  squeezed sliver, so its 4 links settle into 2 rows of 2 instead of 4
  stacked rows.
- **44px tap targets at narrow width**: `.main-nav__link { min-height: 44px; }`
  inside the same `≤480px` query.

### The one file outside `app.css`, and why

`.logout-button` (in `login.component.css`) measured 33px tall at 375px -
under the 44px requirement. Angular's style encapsulation means `app.css`
(App's own scoped stylesheet) genuinely cannot reach `.logout-button` -
it lives inside `LoginComponent`'s own template/encapsulation scope, so a
selector for it in `app.css` would compile but never match the real DOM
element. Given the task's own explicit, numbered constraint ("Tap targets
stay at least 44px tall at narrow widths"), silently leaving this one
control under the stated minimum wasn't the safer choice - added a
narrowly-scoped, sizing-only override in `login.component.css`
(`min-height: 44px` inside the same `≤480px` query), touching nothing
about its color, font, or Pass 2 styling. Flagging this explicitly rather
than let a Pass-2-owned file change go unmentioned.

## Verification

All 23 checks below at all 7 widths, live against the running app -
`evidence/verify-log.txt`, screenshots `evidence/fixed-*.png`:

- **`document.scrollWidth <= document.clientWidth`** at every one of the 7
  widths - asserted programmatically, not eyeballed.
- **Logout button's bounding-box `right` edge <= viewport width** at every
  width - the actual failure mode from the diagnosis, checked directly
  rather than inferred from the scrollWidth metric (which, per the
  diagnosis above, wouldn't have caught it).
- **`.main-nav__link--active` computed background is non-transparent** at
  every width - confirmed the active-state highlight (`rgba(204, 58, 99, 0.18)`,
  i.e. `--rose` at 18% alpha) survives the reflow at every tier, not just
  visually spot-checked at one width.
- **All 4 nav links measure exactly 44px tall at 375px** (was 36px before
  the fix).
- **axe (`wcag2a`+`wcag2aa`) at 375px specifically: 0 violations**
  (`evidence/axe-375-topbar-fix.json`) - re-run after the reflow, per the
  instruction that focus order and touch-target sizing can regress when
  layout changes, not assumed safe because Pass 2's axe run happened to
  pass at a different viewport.

23/23 passed.

## Dev server

Left running per your request (`localhost:4200`) - not stopped at the end
of this task, unlike every other task in this session.
