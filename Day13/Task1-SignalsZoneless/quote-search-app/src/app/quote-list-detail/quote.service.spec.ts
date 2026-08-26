import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuoteService } from './quote.service';
import { QuoteListResponse } from '../models/quote.models';

// Characterization test: pins the REAL contract of the live API
// (Day5/Task6-Resilience/QuotesApi, http://localhost:5041) before any
// interceptor or error-mapping code touches it. Fixtures below are verbatim
// response bodies captured via curl against the running server this
// session, not invented shapes. Uses HttpTestingController rather than a
// live HTTP call so the test stays deterministic, doesn't require the API
// process to be up to run in CI, and doesn't write throwaway rows into the
// real dev database on every run.
describe('QuoteService (contract characterization)', () => {
  let service: QuoteService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(QuoteService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('parses the real GET /api/quotes?page=1&size=10 response shape', () => {
    // Verbatim (trimmed to 3 of the real 10 items) from:
    //   curl "http://localhost:5041/api/quotes?page=1&size=10"
    const realResponse: QuoteListResponse = {
      page: 1,
      size: 10,
      total: 27,
      items: [
        {
          id: 2,
          author: 'Ada Lovelace',
          text: 'That brain of mine is something more than merely mortal.',
          createdAtUtc: '2026-03-11T14:30:00',
          ownerId: 1,
        },
        {
          id: 4,
          author: 'Grace Hopper',
          text: "The most dangerous phrase in the language is: We've always done it this way.",
          createdAtUtc: '2026-02-02T10:00:00',
          ownerId: 1,
        },
        {
          id: 11,
          author: 'Ada Lovelace',
          text: 'CQRS test quote',
          createdAtUtc: '2026-08-22T04:33:29.4132847',
          ownerId: 1,
        },
      ],
    };

    let actual: QuoteListResponse | undefined;
    service.getQuotes(1, 10).subscribe((res) => (actual = res));

    const req = httpMock.expectOne(
      (r) =>
        r.url === 'http://localhost:5041/api/quotes' &&
        r.params.get('page') === '1' &&
        r.params.get('size') === '10',
    );
    expect(req.request.method).toBe('GET');
    req.flush(realResponse);

    expect(actual).toEqual(realResponse);
    expect(actual?.items[0]).toEqual({
      id: 2,
      author: 'Ada Lovelace',
      text: 'That brain of mine is something more than merely mortal.',
      createdAtUtc: '2026-03-11T14:30:00',
      ownerId: 1,
    });
  });

  it('surfaces the real 400 ValidationProblemDetails shape for an invalid POST /api/quotes', () => {
    // Verbatim from an authenticated request (the endpoint requires a
    // bearer token; this pins the *validation* error shape, distinct from
    // the 401 unauthenticated case):
    //   curl -X POST http://localhost:5041/api/quotes \
    //     -H "Authorization: Bearer <token>" -d '{"author":"","text":"some text"}'
    const realValidationError = {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { Author: ['The Author field is required.'] },
      traceId: '00-c66a70b55009410ea7033f6bd8908f10-850a00f1650db0b9-01',
    };

    let nextCalled = false;
    let capturedError: HttpErrorResponse | undefined;
    service.createQuote({ author: '', text: 'some text' }).subscribe({
      next: () => {
        nextCalled = true;
      },
      error: (err: HttpErrorResponse) => {
        capturedError = err;
      },
    });

    const req = httpMock.expectOne('http://localhost:5041/api/quotes');
    expect(req.request.method).toBe('POST');
    req.flush(realValidationError, { status: 400, statusText: 'Bad Request' });

    expect(nextCalled).toBe(false);
    expect(capturedError?.status).toBe(400);
    expect(capturedError?.error).toEqual(realValidationError);
  });
});
