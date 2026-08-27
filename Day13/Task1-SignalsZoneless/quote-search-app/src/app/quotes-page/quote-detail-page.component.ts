import { Component, inject, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { EMPTY, catchError, switchMap } from 'rxjs';
import { QuoteService } from '../quote-list-detail/quote.service';
import { Quote } from '../models/quote.models';
import { mapHttpErrorToAppError } from '../http/app-error';

type DetailOutcome = 'loading' | 'invalid' | 'not-found' | 'error' | 'found';

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
  private readonly quoteService = inject(QuoteService);

  // Bound directly from the :id path segment - requires
  // withComponentInputBinding() in provideRouter (app.config.ts).
  readonly id = input.required<string>();

  readonly outcome = signal<DetailOutcome>('loading');
  readonly message = signal<string | null>(null);
  readonly quote = signal<Quote | null>(null);

  // Same stale-response race fix as the existing QuoteDetailComponent
  // (quote-list-detail/quote-detail.component.ts): id as an Observable
  // piped through switchMap, so navigating from /quotes/2 to /quotes/3
  // before 2's request resolves cancels 2's in-flight HTTP call rather
  // than letting it land after 3's.
  constructor() {
    toObservable(this.id)
      .pipe(
        switchMap((raw) => {
          this.quote.set(null);
          this.message.set(null);

          const parsedId = parseQuoteId(raw);

          // Outcome (a): reject client-side before ever calling the API.
          if (parsedId === null) {
            this.outcome.set('invalid');
            this.message.set(`"${raw}" isn't a valid quote id.`);
            return EMPTY;
          }

          this.outcome.set('loading');

          return this.quoteService.getQuoteById(parsedId).pipe(
            catchError((err: HttpErrorResponse) => {
              // Reuses the existing typed error mapper (http/app-error.ts)
              // rather than re-deriving a message by hand - for a 404 this
              // surfaces the server's own ProblemDetails `detail` text
              // ("No quote exists with id N.") verbatim.
              const appError = mapHttpErrorToAppError(err, {
                fallbackMessage: 'Failed to load quote detail.',
              });

              // Outcome (b): a real 404 from the server - distinct state
              // and message from the client-side "invalid" rejection above.
              this.outcome.set(err.status === 404 ? 'not-found' : 'error');
              this.message.set(appError.friendlyMessage);
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((quote) => {
        // Outcome (c): found.
        this.outcome.set('found');
        this.quote.set(quote);
      });
  }
}
