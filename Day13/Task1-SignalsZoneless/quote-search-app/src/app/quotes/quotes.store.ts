import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Quote, QuoteListResponse } from '../models/quote.models';

const QUOTES_URL = 'http://localhost:5041/api/quotes';

// Relocated out of QuoteSearchComponent (unchanged behavior/output) so the
// new Collections feature can join against the same live quotes list
// in-memory instead of maintaining its own separate copy that could drift
// out of sync after an add/delete.
@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly http = inject(HttpClient);

  readonly quotes = signal<Quote[]>([]);

  // Plain constructor call, not effect(): this fetch has no signal
  // dependency (page/size are fixed, GET /api/quotes is anonymous and
  // doesn't vary with anything this service holds), so there is nothing
  // for an effect to react to.
  constructor() {
    this.refetch();
  }

  refetch(): void {
    this.http
      .get<QuoteListResponse>(QUOTES_URL, { params: { page: 1, size: 50 } })
      .subscribe((res) => this.quotes.set(res.items));
  }

  addQuote(quote: Quote): void {
    this.quotes.update((qs) => [quote, ...qs]);
  }

  removeQuote(id: number): void {
    this.quotes.update((qs) => qs.filter((q) => q.id !== id));
  }
}
