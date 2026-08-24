import { Component, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { AddQuoteComponent } from '../add-quote/add-quote.component';
import { AuthService } from '../auth/auth.service';
import { Quote, QuoteListResponse } from '../models/quote.models';

const QUOTES_URL = 'http://localhost:5041/api/quotes';

interface AuthorGroup {
  author: string;
  quotes: Quote[];
}

@Component({
  selector: 'app-quote-search',
  standalone: true,
  imports: [AddQuoteComponent, DatePipe],
  templateUrl: './quote-search.component.html',
  styleUrl: './quote-search.component.css',
})
export class QuoteSearchComponent {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  readonly currentUserId = this.auth.currentUserId;

  quotes = signal<Quote[]>([]);
  searchTerm = signal<string>('');
  deleteError = signal<string | null>(null);

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

  constructor() {
    this.refetch();
  }

  private refetch(): void {
    this.http
      .get<QuoteListResponse>(QUOTES_URL, { params: { page: 1, size: 50 } })
      .subscribe((res) => this.quotes.set(res.items));
  }

  onSearchInput(event: Event): void {
    this.searchTerm.set((event.target as HTMLInputElement).value);
  }

  onQuoteAdded(quote: Quote): void {
    this.quotes.update((qs) => [quote, ...qs]);
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
      next: () => this.quotes.update((qs) => qs.filter((q) => q.id !== quote.id)),
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
}
