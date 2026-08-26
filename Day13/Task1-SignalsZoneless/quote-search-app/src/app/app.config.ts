import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './auth/auth.interceptor';
import { retryInterceptor } from './http/retry.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // auth first (outermost) so it sees the final post-retry error and can
    // still react to a persistent 401; retry closest to the backend so it's
    // the one resending the actual HTTP call on transient failures.
    provideHttpClient(withInterceptors([authInterceptor, retryInterceptor])),
  ],
};
