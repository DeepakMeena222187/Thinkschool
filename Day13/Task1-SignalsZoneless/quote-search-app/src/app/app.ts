import { Component, inject } from '@angular/core';
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

  onQuoteCreated(quote: Quote): void {
    this.quotesStore.addQuote(quote);
  }
}
