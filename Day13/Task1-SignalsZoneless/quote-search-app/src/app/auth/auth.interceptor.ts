import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

// Functional interceptor: inject() works here because Angular runs
// interceptor functions inside an injection context.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.accessToken();

  const authorizedReq = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(authorizedReq).pipe(
    catchError((error: HttpErrorResponse) => {
      // A 401 on a request that carried a token means the stored token is
      // expired/invalid (a real risk since it's persisted in localStorage
      // across sessions with no refresh flow here) - clear it so the UI
      // falls back to the login screen instead of staying in a "looks
      // logged in but every request 401s" state.
      if (error.status === 401 && token) {
        auth.logout();
      }
      return throwError(() => error);
    }),
  );
};
