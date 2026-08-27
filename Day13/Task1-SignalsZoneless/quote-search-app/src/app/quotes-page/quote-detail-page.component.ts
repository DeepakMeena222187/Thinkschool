import { Component, computed, effect, inject, input } from '@angular/core';
import { QuotesStore } from '../quotes/quotes.store';

// The Angular route param is always a string with no server-side {id:int}
// equivalent - ASP.NET rejects a non-int id at route matching (confirmed:
// no 400 for /api/quotes/abc, the route just never matches), but the
// Angular route ':id' segment will happily capture "abc". This regex is
// what gives us that same rejection on the client, before any HTTP call.
function parseQuoteId(raw: string): number | null {
  return /^\d+$/.test(raw) ? Number(raw) : null;
}

@Component({
  selector: 'app-quote-detail-page',
  imports: [],
  templateUrl: './quote-detail-page.component.html',
  styleUrl: './quote-detail-page.component.css',
})
export class QuoteDetailPageComponent {
  private readonly quotesStore = inject(QuotesStore);

  // Bound directly from the :id path segment - requires
  // withComponentInputBinding() in provideRouter (app.config.ts).
  readonly id = input.required<string>();

  // Parsing (not fetching) stays a component concern - it's route-param
  // presentation logic, not quotes domain state, so it doesn't belong in
  // the store. Null means the current :id segment isn't a valid integer.
  readonly parsedId = computed(() => parseQuoteId(this.id()));

  readonly detailQuote = this.quotesStore.detailQuote;
  readonly detailError = this.quotesStore.detailError;

  // The store's detailStatus is shared/global - if this component didn't
  // override it for the invalid-id case, navigating from a valid detail
  // (e.g. /quotes/2, status 'found') straight to an invalid one
  // (/quotes/abc) would keep showing the *previous* quote, because
  // loadById() is never called and the store's state simply doesn't
  // change. 'invalid' here takes priority over whatever the store last
  // held, regardless of what that was.
  readonly outcome = computed(() => (this.parsedId() === null ? 'invalid' : this.quotesStore.detailStatus()));

  constructor() {
    // Re-issues the store's switchMap-guarded fetch every time :id changes
    // to a valid integer - a stale slower request for a previous id can't
    // clobber a newer one (see QuotesStore's constructor comment for the
    // concurrency mechanism). An invalid id (parsedId() === null) simply
    // never calls loadById() - no request happens, no stale store state to
    // clear either, so the previous quote just stays behind the invalid
    // message the template shows for that case.
    effect(() => {
      const parsed = this.parsedId();
      if (parsed !== null) {
        this.quotesStore.loadById(parsed);
      }
    });
  }
}
