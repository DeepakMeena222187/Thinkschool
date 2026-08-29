import { Component, computed, effect, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { AddQuoteComponent } from '../add-quote/add-quote.component';
import { CollectionsComponent } from '../collections/collections.component';
import { CollectionsStore } from '../collections/collections.store';
import { QuotesStore, deriveListView } from '../quotes/quotes.store';
import { AuthService } from '../auth/auth.service';
import { Quote } from '../models/quote.models';
import { environment } from '../../environments/environment';

const QUOTES_URL = `${environment.apiBaseUrl}/api/quotes`;

interface AuthorGroup {
  author: string;
  quotes: Quote[];
}

type DashboardTab = 'quotes' | 'collections';

@Component({
  selector: 'app-quote-search',
  standalone: true,
  imports: [AddQuoteComponent, CollectionsComponent, DatePipe],
  templateUrl: './quote-search.component.html',
  styleUrl: './quote-search.component.css',
})
export class QuoteSearchComponent {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly quotesStore = inject(QuotesStore);
  private readonly collectionsStore = inject(CollectionsStore);

  readonly currentUserId = this.auth.currentUserId;
  readonly isAuthenticated = this.auth.isAuthenticated;

  // The dashboard's own view - fixed at page 1, size 50, entirely unrelated
  // to whatever page/size the routed list page happens to be viewing. Same
  // deriveListView() mechanism as that page uses, not a special case: this
  // is what makes the two views independent by construction rather than by
  // convention (see Day16/Task2-SignalsState/README.md for the bug this
  // replaced, where both shared one store-level page/size cursor).
  private readonly dashboardPage = signal(1);
  private readonly dashboardSize = signal(50);
  private readonly dashboardView = deriveListView(this.quotesStore, this.dashboardPage, this.dashboardSize);
  readonly quotes = this.dashboardView.items;

  readonly collections = this.collectionsStore.collections;

  searchTerm = signal<string>('');
  deleteError = signal<string | null>(null);
  addToCollectionError = signal<string | null>(null);

  activeTab = signal<DashboardTab>('quotes');

  // Accordion state: which author's panel is open. null = all collapsed.
  expandedAuthor = signal<string | null>(null);

  filteredQuotes = computed(() =>
    this.quotes().filter((q) =>
      q.author.toLowerCase().includes(this.searchTerm().toLowerCase()),
    ),
  );

  // Derives from filteredQuotes(), so a search term still narrows results
  // before grouping - groups (and their counts) always reflect the
  // current filter. Sorted alphabetically by author for a stable,
  // predictable accordion order regardless of fetch/insert order.
  groupedQuotes = computed<AuthorGroup[]>(() => {
    const byAuthor = new Map<string, Quote[]>();

    for (const quote of this.filteredQuotes()) {
      const bucket = byAuthor.get(quote.author);
      if (bucket) {
        bucket.push(quote);
      } else {
        byAuthor.set(quote.author, [quote]);
      }
    }

    return [...byAuthor.entries()]
      .map(([author, quotes]) => ({ author, quotes }))
      .sort((a, b) => a.author.localeCompare(b.author));
  });

  // Plain constructor call, not effect(): dashboardPage/dashboardSize are
  // fixed and never change, so there's no signal dependency to react to -
  // this is what actually produces "loaded at bootstrap" now that
  // QuotesStore itself no longer self-initiates any fetch.
  constructor() {
    this.quotesStore.loadPage(this.dashboardPage(), this.dashboardSize());
  }

  setTab(tab: DashboardTab): void {
    this.activeTab.set(tab);
  }

  onSearchInput(event: Event): void {
    this.searchTerm.set((event.target as HTMLInputElement).value);
  }

  onQuoteAdded(quote: Quote): void {
    this.quotesStore.addQuote(quote);
  }

  toggleAuthor(author: string): void {
    this.expandedAuthor.update((current) => (current === author ? null : author));
  }

  onDeleteQuote(quote: Quote): void {
    const confirmed = confirm(`Delete this quote by ${quote.author}?`);
    if (!confirmed) {
      return;
    }

    this.deleteError.set(null);

    this.http.delete(`${QUOTES_URL}/${quote.id}`).subscribe({
      next: () => this.quotesStore.removeQuote(quote.id),
      error: (err: HttpErrorResponse) => {
        this.deleteError.set(
          err.status === 403
            ? 'You can only delete your own quotes.'
            : err.status === 404
              ? 'That quote no longer exists.'
              : 'Failed to delete quote.',
        );
      },
    });
  }

  onAddToCollection(quoteId: number, event: Event): void {
    const select = event.target as HTMLSelectElement;
    const collectionId = Number(select.value);
    select.value = '';

    if (!collectionId) {
      return;
    }

    this.addToCollectionError.set(null);
    this.collectionsStore.addItem(collectionId, quoteId).subscribe({
      error: () => this.addToCollectionError.set('Failed to add quote to collection.'),
    });
  }
}
