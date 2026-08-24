import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../auth/auth.service';

type AuthMode = 'login' | 'signup';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);

  readonly isAuthenticated = this.auth.isAuthenticated;

  mode = signal<AuthMode>('login');

  email = signal('');
  password = signal('');
  confirmPassword = signal('');
  errorMessage = signal<string | null>(null);

  onEmailInput(event: Event): void {
    this.email.set((event.target as HTMLInputElement).value);
  }

  onPasswordInput(event: Event): void {
    this.password.set((event.target as HTMLInputElement).value);
  }

  onConfirmPasswordInput(event: Event): void {
    this.confirmPassword.set((event.target as HTMLInputElement).value);
  }

  toggleMode(): void {
    this.mode.update((current) => (current === 'login' ? 'signup' : 'login'));
    this.errorMessage.set(null);
    this.password.set('');
    this.confirmPassword.set('');
  }

  onSubmit(): void {
    this.errorMessage.set(null);

    if (this.mode() === 'signup') {
      if (this.password() !== this.confirmPassword()) {
        this.errorMessage.set('Passwords do not match.');
        return;
      }

      this.auth.register({ email: this.email(), password: this.password() }).subscribe({
        error: (err: HttpErrorResponse) =>
          this.errorMessage.set(
            err.status === 409
              ? 'An account with this email already exists.'
              : 'Registration failed. Please try again.',
          ),
      });
      return;
    }

    this.auth.login({ email: this.email(), password: this.password() }).subscribe({
      error: () => this.errorMessage.set('Login failed. Check your email and password.'),
    });
  }

  onLogout(): void {
    this.auth.logout();
  }
}
