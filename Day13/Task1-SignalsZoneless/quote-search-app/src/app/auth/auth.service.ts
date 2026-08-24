import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse } from '../models/quote.models';

const LOGIN_URL = 'http://localhost:5041/api/auth/login';
const REGISTER_URL = 'http://localhost:5041/api/auth/register';
const STORAGE_KEY = 'accessToken';

// Reads the "sub" claim out of a JWT's payload segment - no signature
// verification, no external library. That's fine here: the token is only
// ever used client-side to decide what the UI shows (e.g. a delete
// button); the server independently re-verifies and enforces ownership
// on every request regardless of what the client thinks its user id is.
function decodeUserIdFromJwt(token: string): number | null {
  try {
    const payloadSegment = token.split('.')[1];
    const base64 = payloadSegment.replace(/-/g, '+').replace(/_/g, '/');
    const payload = JSON.parse(atob(base64)) as { sub?: string };
    return payload.sub ? Number(payload.sub) : null;
  } catch {
    return null;
  }
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  // Seeded from localStorage at construction time so a page refresh
  // doesn't silently log the user out. The signal stays the source of
  // truth for the rest of the app's lifetime - setToken()/logout() keep
  // localStorage in sync alongside it on every change, they're never read
  // from again after this initial seed.
  private readonly accessTokenSignal = signal<string | null>(localStorage.getItem(STORAGE_KEY));
  readonly accessToken = this.accessTokenSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.accessTokenSignal() !== null);

  readonly currentUserId = computed(() => {
    const token = this.accessTokenSignal();
    return token ? decodeUserIdFromJwt(token) : null;
  });

  login(credentials: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(LOGIN_URL, credentials)
      .pipe(tap((res) => this.setToken(res.accessToken)));
  }

  // POST /api/auth/register returns the same shape as login (verified via
  // curl: accessToken/refreshToken/expiresIn), so registering logs the
  // user in directly - no separate follow-up login call needed.
  register(credentials: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(REGISTER_URL, credentials)
      .pipe(tap((res) => this.setToken(res.accessToken)));
  }

  logout(): void {
    this.accessTokenSignal.set(null);
    localStorage.removeItem(STORAGE_KEY);
  }

  private setToken(token: string): void {
    this.accessTokenSignal.set(token);
    localStorage.setItem(STORAGE_KEY, token);
  }
}
