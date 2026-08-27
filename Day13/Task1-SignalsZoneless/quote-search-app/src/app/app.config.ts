import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { authInterceptor } from './auth/auth.interceptor';
import { retryInterceptor } from './http/retry.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(
      routes,
      // Lets QuoteDetailPageComponent's `id` input bind directly to the
      // :id route segment instead of reading ActivatedRoute by hand.
      withComponentInputBinding(),
      withViewTransitions({
        // The native View Transitions API has no built-in reduced-motion
        // opt-out - skipTransition() falls straight back to a plain DOM
        // swap with no animation at all, which is what
        // prefers-reduced-motion: reduce asks for.
        onViewTransitionCreated: ({ transition }) => {
          if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
            transition.skipTransition();
          }
        },
      }),
    ),
    // auth first (outermost) so it sees the final post-retry error and can
    // still react to a persistent 401; retry closest to the backend so it's
    // the one resending the actual HTTP call on transient failures.
    provideHttpClient(withInterceptors([authInterceptor, retryInterceptor])),
  ],
};
