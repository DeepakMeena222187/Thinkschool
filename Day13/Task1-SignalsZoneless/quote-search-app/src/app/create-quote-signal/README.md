# Create Quote (Signal Forms) — Day 14 Task 2

The same create-quote feature as [`../create-quote/`](../create-quote/README.md),
rebuilt on Angular's newer `@angular/forms/signals` API (`FieldTree`, `form()`,
`validate()`, `[formRoot]`/`[formField]`) instead of `ReactiveFormsModule`. Kept as a
separate component rather than replacing the reactive-forms version, so both are visible
side by side for comparison — this is the Day 14 forms exercise, layered onto the Day 13
workspace rather than a rewrite of Day 13's work.

## Status: verified working, not just written

Given the instruction to say plainly if this isn't actually finished — it is. It was
exercised end to end with a Playwright script against the real running app and the real
API just now, not left as "should work" from reading the code:

- **Blank submit** → both fields show `role="alert"` errors ("Author is required.",
  "Quote text is required."), `aria-invalid="true"` is set, and focus moves to the
  `author` field — confirming `onInvalid()`'s manual `markAsTouched()` +
  `focusBoundControl()` calls actually fire (Signal Forms doesn't auto-touch fields the
  way `FormGroup.markAllAsTouched()` does, so this was written explicitly and needed
  checking).
- **Whitespace-only author (`"   "`)** → still rejected as "Author is required.",
  confirming the custom `validate()` check (not the built-in `required()`) is what's
  actually running.
- **Valid submit** → real `POST /api/quotes` fired, success banner rendered
  (`Quote added: "..." — Playwright Verification Author`), no error banner, and a
  follow-up `GET /api/quotes` confirmed the quote was actually persisted server-side, not
  just optimistically rendered. The form then reset (`author` field value empty
  afterward).
- **Zero console/page errors** through all of the above.

**What was not separately re-tested here**: the `maxLength` exceed-the-limit error path
(101+ char author / 1001+ char text) and the server-error field-routing path (a 400 from
the real API landing on the correct field via `fieldTree` in `mapServerError`) weren't
re-driven in this verification pass — they're structurally the same code path as the
required-field check already confirmed above, but weren't independently exercised, so
they're not claimed as separately verified.

## What was built

`CreateQuoteSignalComponent` mirrors `CreateQuoteComponent` field-for-field (`author`,
`text`, same 100/1000 limits, same success/error banners, same `aria-invalid`/
`aria-describedby` wiring, same skip-link-reachable structure), but the model and
validation are expressed through Signal Forms instead of Reactive Forms:

```ts
private readonly model = signal<CreateQuoteRequest>({ author: '', text: '' });

readonly quoteForm: FieldTree<CreateQuoteRequest> = form(this.model, (p) => {
  validate(p.author, ({ value }) => { /* required + maxLength, custom */ });
  validate(p.text, ({ value }) => { /* same */ });
}, {
  submission: {
    action: async () => { /* POST via QuoteService.createQuote(), same as reactive version */ },
    onInvalid: () => { /* manual markAsTouched() + focusBoundControl() */ },
  },
});
```

The signal (`model`) *is* the form's data model — there's no separate `FormGroup` copy of
the values the way Reactive Forms needs one; `form()` wraps the signal directly and the
returned `FieldTree` is a live, structured view over it.

## Why a custom `validate()` instead of the built-in `required()`/`maxLength()`

**`required()`**: Signal Forms' built-in `required()` validator doesn't document
whitespace-trimming behavior. The server's `[Required]` attribute trims first (confirmed
via the same curl test as the reactive-forms version:
`author: "   "` → 400 "The Author field is required."). Rather than trust an undocumented
behavior to match, this uses the same whitespace-aware check as `requiredNotBlank()` in
the reactive-forms version, so both implementations reject the same inputs for the same
reason.

**`maxLength()`**: deliberately *not* used, for a concrete, checked reason recorded in
the code comment — Signal Forms' `maxLength()` binds `FieldState.maxLength` straight onto
the native `<input maxlength>` DOM attribute. That makes the browser silently cap real
typing/pasting at the limit, so the "too long" error becomes unreachable through any
normal interaction (a user physically cannot type past 100 characters once the attribute
is set) — unlike the reactive-forms version, where `Validators.maxLength(100)` sets no
native attribute, so a user *can* type past 100 characters and see the resulting error.
Using a custom `validate()` instead (which doesn't touch `FieldState.maxLength`)
reproduces the reactive-forms version's actual behavior instead of silently changing it.

## Server-error routing: a real difference from the reactive-forms version

The reactive-forms version dumps every server validation message into one flat error
banner. This version routes each field's server error onto the matching real field via
`fieldTree`:

```ts
return Object.entries(problem.errors).flatMap(([field, messages]) => {
  const target = field === 'Author' ? this.quoteForm.author
                : field === 'Text' ? this.quoteForm.text
                : undefined;
  return messages.map((message) => ({ kind: 'server', message, fieldTree: target }));
});
```

So a server-side `Author` error renders through the same `errors()` the client-side
`author` field already displays, rather than a separate always-flat banner — a genuine
capability difference between the two form systems this exercise surfaces, not just a
styling difference.

## Reused `QuoteService.createQuote()`

Same reasoning as [`create-quote/README.md`](../create-quote/README.md#reused-quoteservicecreatequote-and-why):
one `HttpClient` wrapper against `http://localhost:5041/api/quotes`, shared by both the
reactive-forms and Signal Forms versions, rather than a third copy of the same POST call.

## Dependency note

`@angular/forms/signals` ships from the same `@angular/forms` package already in
`package.json` (`^21.2.0`) — no separate package was added. `ng build` (both
`--configuration development` and the default production config) compiles this component
with 0 errors as of this writing.
