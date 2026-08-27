import { Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../auth/auth.service';
import { Collection, Quote } from '../models/quote.models';
import { CollectionsStore } from './collections.store';
import { QuotesStore, deriveListView } from '../quotes/quotes.store';

@Component({
  selector: 'app-collections',
  standalone: true,
  imports: [],
  templateUrl: './collections.component.html',
  styleUrl: './collections.component.css',
})
export class CollectionsComponent {
  private readonly auth = inject(AuthService);
  private readonly collectionsStore = inject(CollectionsStore);
  private readonly quotesStore = inject(QuotesStore);

  readonly isAuthenticated = this.auth.isAuthenticated;
  readonly currentUserId = this.auth.currentUserId;
  readonly collections = this.collectionsStore.collections;

  newCollectionName = signal('');
  createError = signal<string | null>(null);
  deleteError = signal<string | null>(null);
  expandedCollectionId = signal<number | null>(null);

  // Its own view - fixed page 1, size 50, same as the dashboard's, but
  // independently owned and independently fetched. Choosing the same
  // page/size as QuoteSearchComponent means this naturally reads the same
  // cache key (deliberate, since both want "everything"), not a shared
  // cursor either of them could accidentally move for the other.
  private readonly page = signal(1);
  private readonly size = signal(50);
  private readonly view = deriveListView(this.quotesStore, this.page, this.size);

  // A quoteId -> Quote lookup, recomputed whenever this view's items
  // change. Collection items only carry a bare quoteId (the API doesn't
  // return joined quote data), so this is the in-memory join; a missing
  // entry (already-deleted quote) resolves to undefined, which the
  // template renders as "Quote no longer available" instead of crashing or
  // showing blank.
  readonly quoteById = computed(() => {
    const map = new Map<number, Quote>();
    for (const quote of this.view.items()) {
      map.set(quote.id, quote);
    }
    return map;
  });

  // Plain constructor call, not effect(): page/size are fixed and never
  // change, so there's no signal dependency to react to.
  constructor() {
    this.quotesStore.loadPage(this.page(), this.size());
  }

  toggleExpanded(collectionId: number): void {
    this.expandedCollectionId.update((current) => (current === collectionId ? null : collectionId));
  }

  onNameInput(event: Event): void {
    this.newCollectionName.set((event.target as HTMLInputElement).value);
  }

  onCreate(): void {
    const ownerId = this.auth.currentUserId();
    const name = this.newCollectionName().trim();
    if (ownerId === null || !name) {
      return;
    }

    this.createError.set(null);
    this.collectionsStore.create({ name, ownerId }).subscribe({
      next: () => this.newCollectionName.set(''),
      error: () => this.createError.set('Failed to create collection.'),
    });
  }

  onRemoveItem(collectionId: number, quoteId: number): void {
    this.collectionsStore.removeItem(collectionId, quoteId).subscribe();
  }

  onDeleteCollection(collection: Collection): void {
    const confirmed = confirm(`Delete the collection "${collection.name}"?`);
    if (!confirmed) {
      return;
    }

    this.deleteError.set(null);

    this.collectionsStore.deleteCollection(collection.id).subscribe({
      error: (err: HttpErrorResponse) => {
        this.deleteError.set(
          err.status === 403
            ? 'You can only delete your own collections.'
            : err.status === 404
              ? 'That collection no longer exists.'
              : 'Failed to delete collection.',
        );
      },
    });
  }
}
