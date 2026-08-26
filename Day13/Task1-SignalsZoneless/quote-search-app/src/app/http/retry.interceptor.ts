import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { retry, throwError, timer } from 'rxjs';

const MAX_RETRY_ATTEMPTS = 3;
const BASE_DELAY_MS = 200;

// Only GET is idempotent here - POST/DELETE are never retried, since
// blindly repeating them risks e.g. creating the same quote twice.
function isRetryable(req: { method: string }): boolean {
  return req.method === 'GET';
}

// status 0 = no response at all (network error, connection refused, CORS
// preflight failure) and 5xx = server-side failure - both are transient and
// worth retrying. Any other status is the server rejecting the request on
// its merits (e.g. 400/404/401); retrying that would just repeat the same
// failure and mask a real bug, so it's never retried.
function isTransient(error: HttpErrorResponse): boolean {
  return error.status === 0 || error.status >= 500;
}

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isRetryable(req)) {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: MAX_RETRY_ATTEMPTS,
      delay: (error: unknown, retryCount: number) => {
        if (!(error instanceof HttpErrorResponse) || !isTransient(error)) {
          return throwError(() => error);
        }
        // retryCount is 1 on the first retry -> 200ms, 400ms, 800ms.
        return timer(BASE_DELAY_MS * 2 ** (retryCount - 1));
      },
    }),
  );
};
