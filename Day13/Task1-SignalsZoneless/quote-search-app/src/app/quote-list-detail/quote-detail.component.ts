import { Component, inject, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { EMPTY, catchError, switchMap } from 'rxjs';
import { QuoteService } from './quote.service';
import { Quote } from '../models/quote.models';

@Component({
  selector: 'app-quote-detail',
  imports: [],
  templateUrl: './quote-detail.component.html',
  styleUrl: './quote-detail.component.css',
})
export class QuoteDetailComponent {
  private readonly quoteService = inject(QuoteService);

  readonly selectedId = input<number | null>(null);

  readonly loading = signal<boolean>(false);
  readonly error = signal<string | null>(null);
  readonly quote = signal<Quote | null>(null);

  // Stale-response race fix: selectedId (an @Input signal) is turned into
  // an Observable and piped through switchMap. Every time selectedId
  // changes - e.g. click quote A, then quickly click quote B before A's
  // GET resolves - switchMap unsubscribes the in-flight request for A
  // *before* subscribing to B's request. Angular's HttpClient aborts the
  // underlying HTTP call on unsubscribe, so A's response can never reach
  // .subscribe() below even if the network technically returns it later.
  // This is stronger than a manual "ignore responses that don't match the
  // latest request id" counter, which only discards the stale value after
  // it has already arrived - here the stale request is actually cancelled.
  constructor() {
    toObservable(this.selectedId)
      .pipe(
        switchMap((id) => {
          this.quote.set(null);
          this.error.set(null);

          if (id === null) {
            this.loading.set(false);
            return EMPTY;
          }

          this.loading.set(true);

          return this.quoteService.getQuoteById(id).pipe(
            catchError((err: HttpErrorResponse) => {
              this.loading.set(false);
              this.error.set(
                err.status === 404
                  ? `No quote exists with id ${id}.`
                  : 'Failed to load quote detail.',
              );
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((quote) => {
        this.loading.set(false);
        this.quote.set(quote);
      });
  }
}
