# Create Quote (Reactive Forms)

A form for creating a new quote against the real API, built with Angular's
`ReactiveFormsModule` — `FormGroup`/`FormControl`, template-driven validation display, no
`NgModule`. Only renders when `AuthService.isAuthenticated()` is true (creating a quote is
an authenticated action against the API); shows "Log in to create a quote." otherwise.

## What was built

`CreateQuoteComponent` (`create-quote.component.ts/html/css`): a two-field form (`author`,
`text`) with client-side validation matching the server's, submit-state handling
(disabled button + "Adding quote…" while in flight), a success banner, a server-error
banner, and full keyboard/screen-reader wiring (below). On success it emits
`quoteCreated = output<Quote>()` and resets the form.

## Real API contract

```
POST /api/quotes
Authorization: Bearer <token>          (RequireAuthorization(QuotePolicies.CanEditQuotes))
Content-Type: application/json

{ "author": string, "text": string }
```

Validation limits are not invented — they're copied exactly from the server's own
DataAnnotations, `QuotesApi/Contracts/CreateQuoteRequest.cs`:

```csharp
public sealed record CreateQuoteRequest(
    [property: Required, StringLength(100, MinimumLength = 1)] string Author,
    [property: Required, StringLength(1000, MinimumLength = 1)] string Text);
```

→ both fields required, `Author` capped at 100 chars, `Text` capped at 1000. The
component's `authorMaxLength`/`textMaxLength` constants mirror these two numbers.

The real 400 shape was confirmed directly against the running API (authenticated, blank
fields), not assumed:

```
$ curl -X POST http://localhost:5041/api/quotes -H "Authorization: Bearer <token>" \
    -d '{"author":"   ","text":""}'

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1",
 "title":"One or more validation errors occurred.","status":400,
 "errors":{"Author":["The Author field is required."],
           "Text":["The Text field is required."]},
 "traceId":"..."}
```

`describeServerError()` reads `err.error.errors` (a `Record<string, string[]>` keyed by
the C# property's PascalCase name) and joins every message into the error banner; a 401
gets its own "session expired" message instead of the raw validation text.

## The `requiredNotBlank` validator, and why

```ts
function requiredNotBlank(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim().length > 0 ? null : { required: true };
}
```

Angular's built-in `Validators.required` treats a whitespace-only string (`"   "`) as
non-empty — it only rejects the empty string. The server's `[Required]` attribute, by
contrast, trims first: the curl call above (`author: "   "`) came back with `400` and
"The Author field is required.", proving the server rejects whitespace-only input as
missing. Using the built-in validator alone would let a user submit `"   "` client-side,
have it pass local validation, then get rejected by the server anyway with a confusing
round trip. `requiredNotBlank` closes that gap by matching the server's actual behavior
instead of the framework default.

## Reused `QuoteService.createQuote()`, and why

```ts
createQuote(request: CreateQuoteRequest): Observable<Quote> {
  return this.http.post<Quote>(QUOTES_URL, request);
}
```

This lives in `../quote-list-detail/quote.service.ts` — the service originally built for
the Task 2 list+detail feature — rather than a new service in this folder. `QuoteService`
already wraps `HttpClient` against the same `http://localhost:5041/api/quotes` base URL
this form needs; adding a third method to it avoids a second `HttpClient` wrapper with an
identical base URL and identical `Quote`/`CreateQuoteRequest` types. The alternative
(a dedicated `CreateQuoteService`) would have been a real component doing real work, just
duplicated.

## Accessibility wiring

- **Labels**: every input has a real `<label for="...">`, not a placeholder standing in
  for one (`cq-author`/`cq-text` ids, matched `for` attributes).
- **`aria-invalid`**: `[attr.aria-invalid]="showError(control) ? 'true' : null"` — set only
  while a field is actually showing an error, not permanently.
- **`aria-describedby`**: always points at the field's character-count hint
  (`cq-author-hint`); when the field is invalid, it also points at the error message
  (`cq-author-hint cq-author-error`), so a screen reader announces both the hint and the
  error, not just one or the other.
- **Focus management**: `focusFirstInvalidField()` moves focus to the first field that
  actually failed validation on a blocked submit, via `viewChild<ElementRef<...>>` — the
  user doesn't have to hunt for which of two fields is wrong.
- **Live regions**: the error text itself is `role="alert"`; the submitting-state text is
  `aria-live="polite"` inside a visually-hidden paragraph, so screen reader users hear
  "Submitting quote…" even though sighted users just see the button's label change.
- **Success/error banners**: `role="status"` (success) and `role="alert"` (error)
  respectively, so both get announced without needing focus to move to them.

## The skip-link, and why

`app.html` (top level) now opens with:

```html
<a href="#create-quote-heading" class="skip-link">Skip to Create Quote form</a>
```

`.skip-link` is visually off-screen (`left: -9999px`) until it receives keyboard focus,
at which point it becomes visible (`.skip-link:focus { left: 1rem; top: 1rem; ... }`) —
the standard pattern for a link that exists for keyboard/screen-reader users without
cluttering the visual layout for everyone else. It targets `#create-quote-heading`, a
heading given `tabindex="-1"` so it's programmatically focusable even though it's not
naturally interactive. Without this, a keyboard user has to tab through the entire quote
list, the list+detail panels, and everything else above this form just to reach it — the
skip-link makes that a single keypress.

## The quote-list wiring bug: found and fixed

`app.ts` needs to do something with the `Quote` this form emits — otherwise a newly
created quote would only ever appear in the API, never in the already-loaded quote list
the user is looking at, until a manual refresh. The current, correct wiring:

```ts
export class App {
  private readonly quotesStore = inject(QuotesStore);

  onQuoteCreated(quote: Quote): void {
    this.quotesStore.addQuote(quote);
  }
}
```

```html
<app-create-quote (quoteCreated)="onQuoteCreated($event)" />
```

`QuotesStore` (`../quotes/quotes.store.ts`) is the same store `QuoteSearchComponent` reads
its grouped-by-author list from, so this update is immediate and local — no refetch, no
page reload, no drift between "what the form just created" and "what the list shows."

**Verified live, end to end**, not just read as correct: a Playwright script logged in,
submitted a real quote through this form, and confirmed the author's group appeared in
`app-quote-search`'s list within the same tick, with the group count going from 13 to 14
and the new author actually present in the DOM — with zero console errors. The created
quote was also confirmed to have actually persisted server-side via a follow-up
`GET /api/quotes`, not just rendered optimistically on the client.

## The axe DevTools verification

Two screenshots are checked in under `Documentation/`:

- **`preview.webp`** — despite the filename, this is the **before** state: axe DevTools
  (`axe-core 4.12.1`) scanning `http://localhost:4200/`, reporting **21 total issues, all
  21 Critical, 0 Serious/Moderate/Minor**.
- **`axe issue fix.webp`** — the **after** state: the same scan on the same URL reporting
  **0 total issues**.

The 21 critical violations were on the **add-to-collection `<select>` dropdowns** in the
unrelated `quote-search` feature (`quote-search.component.html`) — one per quote card,
each missing an accessible name (a `<select>` with only a placeholder `<option>` and no
`aria-label`/`<label>` reads as unnamed to assistive tech). This is unrelated to the
create-quote form itself, but was caught and fixed in the same accessibility pass. The
fix:

```html
<select
  class="add-to-collection-select"
  aria-label="Add to collection"
  (change)="onAddToCollection(quote.id, $event)"
>
```

One `aria-label` per instance of the repeated `<select>` (one per quote card) accounted
for all 21 flagged instances, re-scanning to 0.
