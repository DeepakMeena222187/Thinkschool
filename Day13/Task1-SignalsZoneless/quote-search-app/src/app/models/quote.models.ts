// Exact shapes confirmed against the running API (Day5/Task6-Resilience/QuotesApi):
//   GET  /api/quotes?page=1&size=50 -> QuoteListResponse
//   POST /api/auth/login            -> LoginResponse
//   POST /api/quotes                -> Quote (the created row)

export interface Quote {
  id: number;
  author: string;
  text: string;
  createdAtUtc: string;
  ownerId: number;
}

export interface QuoteListResponse {
  page: number;
  size: number;
  total: number;
  items: Quote[];
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

export interface CreateQuoteRequest {
  author: string;
  text: string;
}

// GET /api/collections -> Collection[]; POST/DELETE item endpoints ->
// a single updated Collection. The API returns quoteId only, not joined
// quote data - the app joins against QuotesStore's quotes signal itself.
export interface CollectionItem {
  quoteId: number;
  addedAt: string;
}

export interface Collection {
  id: number;
  name: string;
  ownerId: number;
  items: CollectionItem[];
}

export interface CreateCollectionRequest {
  name: string;
  ownerId: number;
}

export interface AddCollectionItemRequest {
  quoteId: number;
}
