import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// Client-side UX boundary only, NOT a security boundary - see
// Day16/Task1-RoutingLazyGuards/README.md. GET /api/quotes and
// GET /api/quotes/{id} are anonymous on the server (verified), so this
// guard only controls what the Angular app navigates to; the data behind
// it is reachable directly (curl, another client) with no token at all.
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
