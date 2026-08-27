import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

// Two separate loadComponent() calls, two separate files, neither statically
// importing the other - each becomes its own lazy chunk. Flat siblings
// rather than a parent/child pair so each route declares its own
// canActivate explicitly (see README for why nesting was rejected).
export const routes: Routes = [
  {
    path: 'quotes',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./quotes-page/quote-list-page.component').then((m) => m.QuoteListPageComponent),
  },
  {
    path: 'quotes/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./quotes-page/quote-detail-page.component').then((m) => m.QuoteDetailPageComponent),
  },
  {
    path: 'login',
    loadComponent: () =>
      import('./login/login-page.component').then((m) => m.LoginPageComponent),
  },
];
