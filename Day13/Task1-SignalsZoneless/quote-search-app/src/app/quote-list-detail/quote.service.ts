import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CreateQuoteRequest, Quote, QuoteListResponse } from '../models/quote.models';
import { environment } from '../../environments/environment';

const QUOTES_URL = `${environment.apiBaseUrl}/api/quotes`;

@Injectable({ providedIn: 'root' })
export class QuoteService {
  private readonly http = inject(HttpClient);

  getQuotes(page: number, size: number): Observable<QuoteListResponse> {
    return this.http.get<QuoteListResponse>(QUOTES_URL, { params: { page, size } });
  }

  getQuoteById(id: number): Observable<Quote> {
    return this.http.get<Quote>(`${QUOTES_URL}/${id}`);
  }

  createQuote(request: CreateQuoteRequest): Observable<Quote> {
    return this.http.post<Quote>(QUOTES_URL, request);
  }
}
