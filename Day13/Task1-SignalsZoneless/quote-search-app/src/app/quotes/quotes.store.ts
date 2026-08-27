import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Subject, catchError, groupBy, map, mergeMap, of, switchMap } from 'rxjs';
import { Quote, QuoteListResponse } from '../models/quote.models';
import { mapHttpErrorToAppError } from '../http/app-error';

const QUOTES_URL = 'http://localhost:5041/api/quotes';

export type QuoteListStatus = 'idle' | 'loading' | 'error' | 'empty' | 'loaded';
export type QuoteDetailStatus = 'idle' | 'loading' | 'error' | 'not-found' | 'found';

type EntryStatus = 'idle' | 'loading' | 'error' | 'loaded';

interface QuoteListEntry {
  items: Quote[];
  total: number;
}

function keyOf(page: number, size: number): string {
  return `${page}:${size}`;
}

// Single source of truth for quotes state (list + single-quote detail).
// The list side is a KEYED cache - one entry per (page,size) actually
// requested - not "fetch everything once and slice client-side." That
// earlier design was rejected: it silently removed real server-side
// pagination (GET /api/quotes?page=N&size=N would only ever be called once,
// with total becoming decorative), and it only worked by accident because
// this app's real dataset (27 rows) fits in a single oversized fetch. At
// real scale it would fetch a subset, slice locally, and quietly show
// wrong data with no error. See Day16/Task2-SignalsState/README.md.
@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly http = inject(HttpClient);

  // Keyed by `${page}:${size}`. NOT a cache in the "skip a hit" sense -
  // loadPage() always issues a real request. This exists purely to isolate
  // different views' data from each other (page 1's response can never land
  // in page 2's slot), not to avoid re-fetching. Named to avoid implying a
  // hit path that doesn't exist.
  private readonly resultsByKey = signal<Map<string, QuoteListEntry>>(new Map());
  private readonly statusByKey = signal<Map<string, EntryStatus>>(new Map());
  private readonly errorByKey = signal<Map<string, string>>(new Map());

  private readonly loadRequests = new Subject<{ page: number; size: number }>();

  // --- Detail state (single quote by id) - unchanged by the keyed-cache
  // redesign above; this was never part of the shared-cursor bug, it was
  // already id-keyed by construction. ---
  readonly detailQuote = signal<Quote | null>(null);
  readonly detailLoading = signal<boolean>(false);
  readonly detailError = signal<string | null>(null);
  readonly detailNotFound = signal<boolean>(false);

  readonly detailStatus = computed<QuoteDetailStatus>(() => {
    if (this.detailLoading()) return 'loading';
    if (this.detailNotFound()) return 'not-found';
    if (this.detailError()) return 'error';
    if (this.detailQuote() !== null) return 'found';
    return 'idle';
  });

  private readonly detailRequests = new Subject<number>();

  constructor() {
    // groupBy splits the request stream by (page,size) key BEFORE
    // switchMap ever sees it, so switchMap only cancels a stale request for
    // the *same* key - a slow page-1 request in flight does not, and
    // architecturally cannot, cancel or overwrite a concurrent page-2
    // request; they're different groups writing to different map entries.
    // This is the actual fix for "an unrelated view's content changed
    // because of navigation in a different view" - not a global switchMap,
    // which would have cancelled unrelated in-flight requests too.
    this.loadRequests
      .pipe(
        groupBy(({ page, size }) => keyOf(page, size)),
        mergeMap((group$) =>
          group$.pipe(
            switchMap(({ page, size }) => {
              const key = keyOf(page, size);
              this.statusByKey.update((m) => new Map(m).set(key, 'loading'));
              this.errorByKey.update((m) => {
                const next = new Map(m);
                next.delete(key);
                return next;
              });

              return this.http.get<QuoteListResponse>(QUOTES_URL, { params: { page, size } }).pipe(
                map((res) => ({ key, ok: true as const, res })),
                catchError((err: HttpErrorResponse) => {
                  const message =
                    err.status === 0
                      ? 'Cannot reach the quotes API. Is it running on http://localhost:5041?'
                      : mapHttpErrorToAppError(err, { fallbackMessage: 'Failed to load quotes.' })
                          .friendlyMessage;
                  return of({ key, ok: false as const, message });
                }),
              );
            }),
          ),
        ),
      )
      .subscribe((result) => {
        if (result.ok) {
          this.resultsByKey.update((m) =>
            new Map(m).set(result.key, { items: result.res.items, total: result.res.total }),
          );
          this.statusByKey.update((m) => new Map(m).set(result.key, 'loaded'));
        } else {
          this.errorByKey.update((m) => new Map(m).set(result.key, result.message));
          this.statusByKey.update((m) => new Map(m).set(result.key, 'error'));
        }
      });

    // Same per-key switchMap-cancellation mechanism, independently, for the
    // detail fetch - a stale /quotes/2 lookup can't clobber a newer
    // /quotes/3 one if the user navigates quickly between two detail views.
    this.detailRequests
      .pipe(
        switchMap((id) => {
          this.detailLoading.set(true);
          this.detailError.set(null);
          this.detailNotFound.set(false);

          return this.http.get<Quote>(`${QUOTES_URL}/${id}`).pipe(
            catchError((err: HttpErrorResponse) => {
              this.detailLoading.set(false);
              if (err.status === 404) {
                this.detailNotFound.set(true);
              } else {
                this.detailError.set(
                  mapHttpErrorToAppError(err, { fallbackMessage: 'Failed to load quote detail.' })
                    .friendlyMessage,
                );
              }
              return of(null);
            }),
          );
        }),
      )
      .subscribe((quote) => {
        if (quote !== null) {
          this.detailLoading.set(false);
          this.detailQuote.set(quote);
        }
      });

    // No self-initiated fetch here anymore - "loaded at bootstrap" is now
    // an emergent property of QuoteSearchComponent (always mounted) asking
    // for its own (1,50) view on construction, the same as any other view
    // would. The store itself only ever reacts to loadPage()/loadById().
  }

  loadPage(page: number, size: number): void {
    this.loadRequests.next({ page, size });
  }

  loadById(id: number): void {
    this.detailQuote.set(null);
    this.detailRequests.next(id);
  }

  entry(page: number, size: number): QuoteListEntry | undefined {
    return this.resultsByKey().get(keyOf(page, size));
  }

  statusOf(page: number, size: number): EntryStatus {
    return this.statusByKey().get(keyOf(page, size)) ?? 'idle';
  }

  errorOf(page: number, size: number): string | null {
    return this.errorByKey().get(keyOf(page, size)) ?? null;
  }

  // Mutates every cached page's entry that happens to contain this quote,
  // so any view currently showing it (dashboard, routed list, whichever
  // page it landed on) reflects the change without a refetch. A quote just
  // created only exists in the caller's hand, not in any fetched page yet,
  // so this only prepends it to the (1, *) entries - the conventional
  // "first page" views - rather than guessing which key should own it.
  addQuote(quote: Quote): void {
    this.resultsByKey.update((m) => {
      const next = new Map(m);
      for (const [key, entry] of next) {
        if (key.startsWith('1:')) {
          next.set(key, { items: [quote, ...entry.items], total: entry.total + 1 });
        }
      }
      return next;
    });
  }

  removeQuote(id: number): void {
    this.resultsByKey.update((m) => {
      const next = new Map(m);
      for (const [key, entry] of next) {
        if (entry.items.some((q) => q.id === id)) {
          next.set(key, {
            items: entry.items.filter((q) => q.id !== id),
            total: entry.total - 1,
          });
        }
      }
      return next;
    });
  }
}

// Pure derivation, not a fetch: slices no data, just reads one specific
// (page,size) entry reactively. page/size are Signals owned by the CALLER
// (each view's own state) - deriveListView never mutates them, it only
// tracks them and re-derives when they change. Two components calling this
// with different page/size signals get fully independent results without
// touching each other's data, because each is reading its own distinct key
// out of QuotesStore's resultsByKey map.
export function deriveListView(
  store: QuotesStore,
  page: Signal<number>,
  size: Signal<number>,
): {
  items: Signal<Quote[]>;
  total: Signal<number | null>;
  status: Signal<QuoteListStatus>;
  error: Signal<string | null>;
} {
  const items = computed(() => store.entry(page(), size())?.items ?? []);
  const total = computed(() => store.entry(page(), size())?.total ?? null);
  const status = computed<QuoteListStatus>(() => {
    const entryStatus = store.statusOf(page(), size());
    if (entryStatus !== 'loaded') return entryStatus;
    return items().length === 0 ? 'empty' : 'loaded';
  });
  const error = computed(() => store.errorOf(page(), size()));

  return { items, total, status, error };
}
