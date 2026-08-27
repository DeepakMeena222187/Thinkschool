import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuotesStore, deriveListView } from '../quotes/quotes.store';

// Deliberately its own file with no static import of QuoteDetailPageComponent
// (or vice versa) - that's what keeps the two route-level loadComponent()
// calls in app.routes.ts as two separate lazy chunks instead of one shared
// eager bundle.
//
// Owns its OWN page/size - a view-local concern, not store state. Reading
// this view's slice of QuotesStore's keyed results never touches or is
// touched by any other view's page/size (e.g. the dashboard's, which is
// fixed at its own 1,50 and reads a completely different cache key).
@Component({
  selector: 'app-quote-list-page',
  imports: [RouterLink],
  templateUrl: './quote-list-page.component.html',
  styleUrl: './quote-list-page.component.css',
})
export class QuoteListPageComponent {
  private readonly quotesStore = inject(QuotesStore);

  // Bound from ?page=N via withComponentInputBinding() (same router
  // feature already used for the :id path param on the detail page - it
  // binds query params too, not just path segments; the input's property
  // name (`page`) is what's matched against the query param key). Genuinely
  // user-facing, not a test-only hook: /quotes?page=999 is a real way to
  // ask this view for a page past the end, and it now issues a real
  // GET /api/quotes?page=999&size=50 - not a client-side array slice.
  readonly pageParam = input<string>('1', { alias: 'page' });
  private readonly page = signal(1);
  // Fixed, local to this view - unrelated to the dashboard's own size.
  private readonly size = signal(50);

  readonly view = deriveListView(this.quotesStore, this.page, this.size);
  readonly status = this.view.status;
  readonly items = this.view.items;
  readonly error = this.view.error;

  constructor() {
    effect(() => {
      const parsed = Number(this.pageParam());
      this.page.set(Number.isInteger(parsed) && parsed > 0 ? parsed : 1);
    });

    effect(() => {
      this.quotesStore.loadPage(this.page(), this.size());
    });
  }
}
