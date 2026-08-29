import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

// Every feature now lives behind a route, each its own loadComponent() call
// in its own file with no static cross-imports between them - that's what
// keeps them as separate lazy chunks instead of one shared eager bundle.
export const routes: Routes = [
  // / was the app's original default view before any routing existed
  // (QuoteSearchComponent rendered unconditionally). Redirecting to /search
  // keeps that the landing experience rather than defaulting to /quotes
  // just because it's the newer route.
  { path: '', redirectTo: 'search', pathMatch: 'full' },
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
    path: 'search',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./quote-search/quote-search.component').then((m) => m.QuoteSearchComponent),
  },
  {
    path: 'collections',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./collections/collections.component').then((m) => m.CollectionsComponent),
  },
  {
    path: 'create',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./create-quote-page/create-quote-page.component').then((m) => m.CreateQuotePageComponent),
  },
  {
    path: 'login',
    loadComponent: () =>
      import('./login/login-page.component').then((m) => m.LoginPageComponent),
  },
];
