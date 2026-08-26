import { HttpErrorResponse, HttpRequest, HttpResponse } from '@angular/common/http';
import { defer, throwError, of } from 'rxjs';
import { retryInterceptor } from './retry.interceptor';

// Real timers rather than fake ones: rxjs's timer() resolves its scheduler
// reference lazily, and in practice doesn't reliably observe vitest's fake
// clock. Worst case here is ~1.4s (200+400+800ms of backoff), acceptable
// for a unit test.
//
// The `next` stubs below wrap their logic in defer() so each retry's
// resubscription re-runs it (incrementing `calls` again) instead of running
// once when `next(req)` is first called - mirroring how HttpClient's real
// backend observable is cold and performs a fresh XHR per subscription.
function waitForOutcome(source: ReturnType<typeof retryInterceptor>) {
  return new Promise<{ response?: HttpResponse<unknown>; error?: HttpErrorResponse }>((resolve) => {
    let response: HttpResponse<unknown> | undefined;
    source.subscribe({
      next: (event) => {
        if (event instanceof HttpResponse) {
          response = event;
        }
      },
      error: (error: HttpErrorResponse) => resolve({ error }),
      complete: () => resolve({ response }),
    });
  });
}

describe('retryInterceptor', () => {
  it(
    'retries a transient 503 GET with backoff and eventually succeeds',
    async () => {
      const req = new HttpRequest('GET', 'http://localhost:5041/api/quotes');
      let calls = 0;
      const next = () =>
        defer(() => {
          calls++;
          return calls < 3
            ? throwError(() => new HttpErrorResponse({ status: 503 }))
            : of(new HttpResponse({ status: 200, body: { ok: true } }));
        });

      const outcome = await waitForOutcome(retryInterceptor(req, next));

      expect(calls).toBe(3);
      expect(outcome.error).toBeUndefined();
      expect(outcome.response?.status).toBe(200);
    },
    10_000,
  );

  it('does not retry a 400 - the request itself was invalid', async () => {
    const req = new HttpRequest('GET', 'http://localhost:5041/api/quotes');
    let calls = 0;
    const next = () =>
      defer(() => {
        calls++;
        return throwError(() => new HttpErrorResponse({ status: 400 }));
      });

    const outcome = await waitForOutcome(retryInterceptor(req, next));

    expect(calls).toBe(1);
    expect(outcome.error?.status).toBe(400);
  });

  it('never retries a POST, even on a transient 503', async () => {
    const req = new HttpRequest('POST', 'http://localhost:5041/api/quotes', { author: 'x', text: 'y' });
    let calls = 0;
    const next = () =>
      defer(() => {
        calls++;
        return throwError(() => new HttpErrorResponse({ status: 503 }));
      });

    const outcome = await waitForOutcome(retryInterceptor(req, next));

    expect(calls).toBe(1);
    expect(outcome.error?.status).toBe(503);
  });

  it(
    'gives up after 3 retries and surfaces the final error if the transient failure persists',
    async () => {
      const req = new HttpRequest('GET', 'http://localhost:5041/api/quotes');
      let calls = 0;
      const next = () =>
        defer(() => {
          calls++;
          return throwError(() => new HttpErrorResponse({ status: 503 }));
        });

      const outcome = await waitForOutcome(retryInterceptor(req, next));

      expect(calls).toBe(4); // 1 initial attempt + 3 retries
      expect(outcome.error?.status).toBe(503);
    },
    10_000,
  );
});
