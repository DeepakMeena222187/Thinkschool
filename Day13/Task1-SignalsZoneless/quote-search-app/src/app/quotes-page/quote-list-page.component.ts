import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { QuoteService } from '../quote-list-detail/quote.service';
import { Quote } from '../models/quote.models';

// Deliberately its own file with no static import of QuoteDetailPageComponent
// (or vice versa) - that's what keeps the two route-level loadComponent()
// calls in app.routes.ts as two separate lazy chunks instead of one shared
// eager bundle.
@Component({
  selector: 'app-quote-list-page',
  imports: [RouterLink],
  templateUrl: './quote-list-page.component.html',
  styleUrl: './quote-list-page.component.css',
})
export class QuoteListPageComponent {
  private readonly quoteService = inject(QuoteService);

  readonly loading = signal<boolean>(false);
  readonly error = signal<string | null>(null);
  readonly quotes = signal<Quote[]>([]);

  // Plain constructor call, not effect(): page/size are fixed and this
  // fetch has no signal dependency to react to (same reasoning already used
  // by QuoteListComponent/QuotesStore elsewhere in this app).
  constructor() {
    this.loading.set(true);

    this.quoteService
      .getQuotes(1, 50)
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (res) => {
          this.quotes.set(res.items);
          this.loading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.error.set(
            err.status === 0
              ? 'Cannot reach the quotes API. Is it running on http://localhost:5041?'
              : 'Failed to load quotes.',
          );
          this.loading.set(false);
        },
      });
  }
}
