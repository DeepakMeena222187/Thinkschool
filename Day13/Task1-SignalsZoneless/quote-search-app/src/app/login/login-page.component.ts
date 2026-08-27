import { Component, effect, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthService } from '../auth/auth.service';

// Thin routed wrapper around the existing LoginComponent - it does not
// duplicate the login form or AuthService (still the single source of
// truth for auth state); it only adds the returnUrl round-trip that only
// makes sense once a route exists to redirect *to* and *from*.
@Component({
  selector: 'app-login-page',
  imports: [LoginComponent],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.css',
})
export class LoginPageComponent {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  constructor() {
    // Fires on creation too, so landing on /login?returnUrl=... while
    // already authenticated (e.g. a stale guard redirect) bounces straight
    // back instead of showing a login form there's no need to fill in.
    effect(() => {
      if (this.auth.isAuthenticated()) {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
        this.router.navigateByUrl(returnUrl);
      }
    });
  }
}
