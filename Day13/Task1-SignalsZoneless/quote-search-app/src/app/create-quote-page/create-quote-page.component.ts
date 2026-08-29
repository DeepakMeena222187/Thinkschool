import { AfterViewInit, Component, ElementRef, inject, viewChild } from '@angular/core';
import { CreateQuoteComponent } from '../create-quote/create-quote.component';
import { CreateQuoteSignalComponent } from '../create-quote-signal/create-quote-signal.component';
import { QuotesStore } from '../quotes/quotes.store';
import { Quote } from '../models/quote.models';

// Routed target for /create. Neither CreateQuoteComponent nor
// CreateQuoteSignalComponent is touched - this only relocates the
// (quoteCreated) handler that used to live on App (app.ts/app.html), which
// had nowhere to attach once these forms stopped being static children of
// App's own template.
@Component({
  selector: 'app-create-quote-page',
  imports: [CreateQuoteComponent, CreateQuoteSignalComponent],
  templateUrl: './create-quote-page.component.html',
  styleUrl: './create-quote-page.component.css',
})
export class CreateQuotePageComponent implements AfterViewInit {
  private readonly quotesStore = inject(QuotesStore);

  // The skip-link in app.html now does routerLink="/create" instead of a
  // same-page href="#create-quote-heading" anchor, since the target no
  // longer lives on the same page. A same-page anchor jump natively moves
  // focus to a tabindex="-1" target; a route navigation does not do that on
  // its own, so focus is moved here explicitly on arrival - otherwise the
  // skip-link would still change the URL but stop doing the one thing a
  // skip link exists for (moving focus past the nav for a keyboard user).
  private readonly heading = viewChild.required<ElementRef<HTMLElement>>('heading');

  ngAfterViewInit(): void {
    this.heading().nativeElement.focus();
  }

  // Moved verbatim from App (app.ts) - same store, same call, same reason
  // (a quote created here should show up in the grouped list immediately,
  // no refetch, no drift).
  onQuoteCreated(quote: Quote): void {
    this.quotesStore.addQuote(quote);
  }
}
