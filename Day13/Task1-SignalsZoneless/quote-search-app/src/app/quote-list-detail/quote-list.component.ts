import { Component, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { QuoteService } from './quote.service';
import { Quote } from '../models/quote.models';

@Component({
  selector: 'app-quote-list',
  imports: [],
  templateUrl: './quote-list.component.html',
  styleUrl: './quote-list.component.css',
})
export class QuoteListComponent {
  private readonly quoteService = inject(QuoteService);

  readonly selectedId = input<number | null>(null);
  readonly quoteSelected = output<number>();

  readonly loading = signal<boolean>(false);
  readonly error = signal<string | null>(null);
  readonly quotes = signal<Quote[]>([]);

  // Plain constructor call, not effect(): page/size are fixed and this
  // fetch has no signal dependency to react to (same reasoning as
  // QuotesStore in the sibling quote-search feature).
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

  onSelect(id: number): void {
    this.quoteSelected.emit(id);
  }
}
