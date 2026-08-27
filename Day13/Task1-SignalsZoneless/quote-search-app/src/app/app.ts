import { Component, computed, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { LoginComponent } from './login/login.component';
import { QuoteSearchComponent } from './quote-search/quote-search.component';
import { QuoteListDetailComponent } from './quote-list-detail/quote-list-detail.component';
import { CreateQuoteComponent } from './create-quote/create-quote.component';
import { CreateQuoteSignalComponent } from './create-quote-signal/create-quote-signal.component';
import { QuotesStore } from './quotes/quotes.store';
import { Quote } from './models/quote.models';

@Component({
  selector: 'app-root',
  imports: [
    RouterLink,
    RouterOutlet,
    LoginComponent,
    QuoteSearchComponent,
    QuoteListDetailComponent,
    CreateQuoteComponent,
    CreateQuoteSignalComponent,
  ],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  // Same store QuoteSearchComponent reads its list from, so a quote created
  // here shows up in the grouped list immediately - no refetch, no drift.
  private readonly quotesStore = inject(QuotesStore);

  private readonly router = inject(Router);

  // Drives hiding the topbar's <app-login/> while the routed /login page
  // (which renders its own <app-login/>) is active - see app.html. Doesn't
  // touch LoginComponent itself, so its behaviour/strings are unaffected;
  // this only decides which of the two places mounts it.
  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  readonly isLoginRoute = computed(() => this.currentUrl().split('?')[0] === '/login');

  onQuoteCreated(quote: Quote): void {
    this.quotesStore.addQuote(quote);
  }
}
