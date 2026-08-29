import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import {
  AddCollectionItemRequest,
  Collection,
  CreateCollectionRequest,
} from '../models/quote.models';
import { environment } from '../../environments/environment';

const COLLECTIONS_URL = `${environment.apiBaseUrl}/api/collections`;

@Injectable({ providedIn: 'root' })
export class CollectionsStore {
  private readonly http = inject(HttpClient);

  readonly collections = signal<Collection[]>([]);

  // Same reasoning as QuotesStore: no signal dependency drives this
  // fetch, so a plain constructor call is correct, not effect().
  constructor() {
    this.refetch();
  }

  refetch(): void {
    this.http.get<Collection[]>(COLLECTIONS_URL).subscribe((res) => this.collections.set(res));
  }

  // Each mutation refetches the full list afterward rather than
  // splicing the response in locally, so the UI always reflects the
  // server's authoritative state after a create/add/remove.
  create(request: CreateCollectionRequest): Observable<Collection> {
    return this.http
      .post<Collection>(COLLECTIONS_URL, request)
      .pipe(tap(() => this.refetch()));
  }

  addItem(collectionId: number, quoteId: number): Observable<Collection> {
    const body: AddCollectionItemRequest = { quoteId };
    return this.http
      .post<Collection>(`${COLLECTIONS_URL}/${collectionId}/items`, body)
      .pipe(tap(() => this.refetch()));
  }

  removeItem(collectionId: number, quoteId: number): Observable<Collection> {
    return this.http
      .delete<Collection>(`${COLLECTIONS_URL}/${collectionId}/items/${quoteId}`)
      .pipe(tap(() => this.refetch()));
  }

  // Unlike the item mutations above, this removes the collection from
  // the signal directly instead of refetching - there's nothing left on
  // the server to refetch, and the caller wants the row gone immediately.
  deleteCollection(id: number): Observable<void> {
    return this.http.delete<void>(`${COLLECTIONS_URL}/${id}`).pipe(
      tap(() => this.collections.update((cs) => cs.filter((c) => c.id !== id))),
    );
  }
}
