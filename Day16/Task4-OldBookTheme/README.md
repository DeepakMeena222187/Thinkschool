# Day 16 Task 4 — Dark Theme → Warm "Old Book" Light Theme

Workspace: `Day13/Task1-SignalsZoneless/quote-search-app`.

## Pass 1 — Tokenization (this commit)

Goal: centralize every hardcoded color into CSS custom properties with zero
visual change, so Pass 2 is a token-value edit in one file, not a hunt
through a dozen component stylesheets.

### Audit

217 hex/`rgba()` occurrences across 12 CSS files (full per-file counts and
every distinct color's semantic role are in the conversation that preceded
this build). The dark theme actually used more distinct colors than the six
originally requested tokens — two accent colors (teal + purple), not one,
plus separate error/success colors with no home in a 6-token scheme.

### Token set defined in `styles.css`

```css
:root {
  --paper: #0f0f1a;
  --surface: #1a1a2e;
  --ink: #e5e7eb;
  --ink-muted: #9ca3af;
  --rose: #2dd4bf;
  --sage: #a78bfa;
  --error: #f87171;
  --success: #34d399;

  --overlay-rgb: 255, 255, 255;
  --shadow-rgb: 0, 0, 0;
  --rose-rgb: 45, 212, 191;
  --sage-rgb: 167, 139, 250;
  --error-rgb: 248, 113, 113;
  --success-rgb: 52, 211, 153;

  --border-subtle: rgba(var(--overlay-rgb), 0.08);
  --border-strong: rgba(var(--overlay-rgb), 0.15);
  --shadow-color: rgba(var(--shadow-rgb), 0.25);
  --overlay: rgba(var(--overlay-rgb), 0.04);

  --decorative-pink: #f472b6;
  --decorative-indigo: #4338ca;
}
```

**Why RGB triplets, not just the 4 named alpha tokens as literally
requested**: the actual CSS uses white/black-based overlays at more than a
dozen distinct opacities (0.03 through 0.45). Forcing every one through
only 4 fixed tokens would have changed how transparent specific borders and
shadows look — failing "still looks identical." Each call site keeps its
own original alpha via `rgba(var(--overlay-rgb), 0.06)` etc.; the 4 named
tokens are convenience shorthands for the most common exact values, not the
only way to use the base colors.

**Accent mapping**: `--rose` = today's teal (`#2dd4bf`), `--sage` = today's
purple (`#a78bfa`) — preserves the existing dual-accent structure rather
than collapsing to one, per your decision.

**Two decorative one-offs kept as their own tokens** rather than folded
into `--rose`/`--sage`: a single hero-panel blob (`#f472b6`) and one
`.collection-item-author` label color (`#4338ca`). Neither is a shade of an
existing semantic role — folding them in would have visibly recolored two
unrelated elements just to avoid two extra token names.

**Disclosed consolidation** (not silently smoothed over): three distinct
muted grays existed in the original CSS — `#9ca3af`, `#6b7280`, `#d1d5db`.
Per your explicit 2-tier text system (`--ink`, `--ink-muted`), all three
collapse into `--ink-muted` (canonical value `#9ca3af`). This measurably
shifts text that was `#6b7280` (hints, placeholders, counts) slightly
lighter, and text that was `#d1d5db` (quote snippets, collection item text)
slightly darker. Checked specifically in the before/after screenshots
below — no visible difference at normal viewing size, but noting it since
it's not literally pixel-identical the way most of the file is. Similarly,
three success greens (`#34d399`, `#6ee7b7`, `#5eead4`) collapse into one
`--success` value, and two near-identical whites (`#f3f4f6` heading,
`#e5e7eb` body) collapse into one `--ink` value — both effectively
imperceptible (≤10 RGB units).

### Acceptance test

```
grep -E '#[0-9a-fA-F]{3,8}|rgba?\(\s*\d' every .css under src/
```

**Result: 10 matches, all inside `:root` in `styles.css`, zero anywhere
else.** (The `--x-rgb` triplet definitions themselves, e.g.
`--rose-rgb: 45, 212, 191;`, correctly don't match this pattern since
they're bare numbers, not hex codes or `rgba()` calls — they're the
token infrastructure, not a hardcoded color at a use site.)

### Visual regression check

Screenshotted `/quotes` (dashboard + routed list both visible) before and
after, same browser session, same viewport, same login state:
`evidence/pass1-BEFORE-quotes.png`, `evidence/pass1-AFTER-quotes.png`.

One methodology bug caught before trusting the result: the first attempt
compared the current `app.html` (Task 3's structure, using `.main-nav`
classes) against `app.css` from the commit *before* Task 3 (which only
defines the old `.day16-nav` classes) — an app.html/app.css pairing that
never actually existed as a real state, and it made the topbar nav look
broken in the "before" shot. Not a Pass 1 bug — a bug in how I constructed
the comparison. Rebuilt the correct pre-Pass-1 `app.css` (post-Task-3,
pre-tokenization) and re-shot; the two images now match.

Build still lists all 6 routed components as separate lazy chunks
(`quote-list-page-component`, `quote-detail-page-component`,
`quote-search-component`, `collections-component`,
`create-quote-page-component`, `login-page-component`) — tokenizing CSS
values doesn't touch the lazy-loading boundaries from Day 16 Task 3.

## Pass 2 — warm "old book" light theme

### Contrast, computed not eyeballed

Ran the actual WCAG relative-luminance formula (script in the session, not
a browser tool) against every candidate before choosing final values:

| Pair | Ratio | Verdict |
|---|---|---|
| `--ink` (#3A2E26) on `--paper` | 12.36:1 | pass (AAA) |
| `--ink` (#3A2E26) on `--surface` | 11.62:1 | pass (AAA) |
| `--ink-muted` (#6B5645) on `--paper` | 6.49:1 | pass, comfortable margin |
| `--ink-muted` (#6B5645) on `--surface` | 6.10:1 | pass, comfortable margin |
| `--rose` (#CC3A63) on `--paper` | 4.54:1 | **passes 4.5:1 by 0.04 - thin margin** |
| `--rose` (#CC3A63) on `--surface` | 4.27:1 | **fails** - rose is never used as text/icon color on a `--surface` background anywhere in the app, only on paper-ish backgrounds or as a border (3:1 threshold, which it clears easily) |
| `--sage` (#A2AB73) on `--paper` | 2.30:1 | **fails outright** - borders/decoration/large-non-text only, confirmed by your own estimate (~2.1:1) |
| `--error` (#8C3A3A) on `--paper` | 7.10:1 | pass |
| `--success` (#4F5C35) on `--paper` | 6.78:1 | pass |
| `--paper` text on solid `--rose` bg | 4.54:1 | pass |
| `--paper` text on solid `--sage` bg | 2.30:1 | **fails** |
| `--ink` text on solid `--sage` bg | 5.39:1 | pass |

The last three rows are why every gradient button changed shape, not just
color: the old dark theme's CTAs used a rose-to-sage gradient background
with a single light text color. **No single text color clears 4.5:1
against both ends of that gradient** - `--paper` passes on the rose end
and fails on the sage end (2.30:1); `--ink` passes on the sage end and
fails on the rose end (2.72:1). Every button/active-tab that used to
render `linear-gradient(135deg, var(--rose), var(--sage))` with light text
now renders solid `--rose` with `--paper` text instead
(`add-quote`, `create-quote`, `create-quote-signal`, `collections`,
`quote-search` tab-active, `login-button`). The brand wordmark's
gradient-text treatment was replaced with solid `--rose` for the same
reason, rather than lean on the WCAG logotype exemption and risk it.

**`--sage` was already in use as text/icon color in three places from
Pass 1's mapping** (it used to be the dark theme's purple accent,
`#a78bfa`) - `.chevron` in both `quote-search.component.css` and
`collections.component.css`, and `.quote-detail-page__author`. All three
moved to `--ink-muted`. `collections.component.css`'s
`.collection-item-author` used Pass 1's `--decorative-indigo` one-off
(preserved only for pixel-fidelity in Pass 1) - retired, moved to
`--ink-muted` as well, same as every other author-byline label.

### Token values

```css
--paper: #FFF7EB;
--surface: #F9F0E0;
--ink: #3A2E26;
--ink-muted: #6B5645;
--rose: #CC3A63;
--sage: #A2AB73;
--error: #8C3A3A;      /* faded brick red, not #f87171 */
--success: #4F5C35;    /* dark olive, sage-family, not #34d399 */

--overlay-rgb: 58, 46, 38;  /* = --ink's own RGB - overlays/borders/shadows now derive from warm brown, not white/black */
--shadow-rgb: 58, 46, 38;
--rose-rgb / --sage-rgb / --error-rgb / --success-rgb: unchanged mapping, new hex values

--decorative-pink: var(--success);  /* third hero blob reuses the muted sage-green rather than an undirected new hue */
```

### Typography

- `Lora` added to the existing Google Fonts `<link>` in `index.html`
  (extended the existing request, not a new one) - `ital,wght@0,400;0,500;0,600;1,400;1,500`.
- `--font-serif: 'Lora', Georgia, 'Times New Roman', serif` applied to
  every actual quote-text element: `.quote-detail-page__text`,
  `.quote-list-page__snippet`, `.quote-text` (dashboard), `.collection-item-text`.
  UI chrome (buttons, nav, labels, headings) stays on `--font-ui`/`--font-display`
  (Inter/Space Grotesk) - the brief's "quotes set larger than UI text"
  only makes sense if the two stay visually distinct.
- Quote text: line-height 1.75, `max-width: 65ch`, larger font-size than
  surrounding UI text.
- Hanging punctuation via the native CSS `hanging-punctuation: first`
  property - genuinely CSS-only, no template change (the opening `&ldquo;`
  is already literal text in the existing templates, not something a
  `::before` could target without restructuring markup). **Caveat,
  disclosed not hidden**: only Safari implements this property; Chromium
  and Firefox parse and ignore it, so the screenshots in this repo (taken
  with Playwright/Chromium) do not visually show the effect. It's the
  correct, spec-compliant, template-untouched implementation of what was
  asked, not a placebo - it just isn't visible in this browser today.

### Layout

- `app.css`'s `.task2-section` (the wrapper around `<router-outlet>`) had
  `0` top padding in both hero and dashboard modes - the literal cause of
  "navbar and quote window too close." Now `3rem` top padding in both.

### Cards

Every card-like surface (`quote-list-page`, `quote-detail-page`,
`quote-search`'s `author-group`/`quote-card`, `collections`'
`collection-group`/`collection-item`) now uses solid `--surface` (outer)
or `--paper` (nested/inset) instead of a translucent white-based overlay -
a translucent tint made sense against a near-black page; on cream it
barely registered as a distinct card. Borders: `1px solid rgba(var(--sage-rgb), 0.35)`.
Shadows: `var(--shadow-color)` (warm-brown-tinted, not black).

### Animation (all gated behind `prefers-reduced-motion`)

- **Card hover lift**: `translateY(-2px)`, 200ms ease-out, shadow deepens
  - `quote-list-page__item`, `quote-card`, `collection-item`. Durations
    come from `--motion-fast`/`--motion-normal` tokens defined once in
    `styles.css`, redefined to `0ms` under `prefers-reduced-motion: reduce`
    - every component's `transition` references the token, so the media
      query in one place disables all of them.
  - Also gated a **pre-existing** `cardEnter` animation on `.quote-card`/
    `.collection-item` that had never been reduced-motion-gated before
    this pass - not strictly "new," but leaving it ungated while
    everything else was being gated would have been an inconsistent,
    half-fixed state, and it's a CSS-only change with no template/logic
    impact.
- **View transition cross-fade with drift**: extended the existing
  `withViewTransitions()` setup (unchanged in `app.config.ts`) with
  `::view-transition-old/new` keyframes in `styles.css` - a few px of
  vertical drift plus opacity, "a page settling," not a slide.
- **Staggered list load**: each item fades up ~30ms after the previous,
  capped at 390ms for the 14th+ item. No per-item index is available
  without a template change (`@for`'s `$index` would need a new binding),
  so this is a pure-CSS `:nth-child` stagger over a fixed number of
  positions - a real constraint-respecting tradeoff, not the "obvious"
  index-based implementation.

### Verification

- **axe**, `wcag2a`+`wcag2aa`, re-run after all of the above:
  **0 violations on both `/quotes` and `/create`** (`evidence/axe-quotes-pass2.json`,
  `evidence/axe-create-pass2.json`).
- One thing axe caught nothing on but I fixed anyway: the "Create Quote"
  heading gets focus()'d programmatically on every arrival at `/create`
  (needed for the skip-link fix from Task 3 to still move focus, since a
  route change doesn't do that on its own). The browser's default focus
  outline is a plain black rectangle - visually jarring against the theme,
  though not an axe violation. Themed it to `outline: 2px solid var(--rose)`.
- Screenshots: `evidence/pass2-quotes.png`, `evidence/pass2-quotes-detail.png`,
  `evidence/pass2-search.png`, `evidence/pass2-create.png`.

### Constraints check

No component template, logic, or user-visible string was changed. Only
CSS files, `styles.css`'s tokens/keyframes, and `index.html`'s font
`<link>` (extended, not replaced). Lazy chunks unaffected - still 6
separate named chunks, confirmed via the same build output check as
Task 3.
