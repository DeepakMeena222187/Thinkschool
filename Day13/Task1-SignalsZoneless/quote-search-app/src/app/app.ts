import { Component, computed, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { LoginComponent } from './login/login.component';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, LoginComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly router = inject(Router);

  // Drives hiding the topbar's <app-login/> while the routed /login page
  // (which renders its own <app-login/>) is active - see app.html. Doesn't
  // touch LoginComponent itself, so its behaviour/strings are unaffected;
  // this only decides which of the two places mounts it.
  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  readonly isLoginRoute = computed(() => this.currentUrl().split('?')[0] === '/login');
}
