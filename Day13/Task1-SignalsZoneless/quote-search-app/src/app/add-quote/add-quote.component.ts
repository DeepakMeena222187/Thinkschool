import { Component, inject, output, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../auth/auth.service';
import { CreateQuoteRequest, Quote } from '../models/quote.models';
import { environment } from '../../environments/environment';

const QUOTES_URL = `${environment.apiBaseUrl}/api/quotes`;

@Component({
  selector: 'app-add-quote',
  standalone: true,
  imports: [],
  templateUrl: './add-quote.component.html',
  styleUrl: './add-quote.component.css',
})
export class AddQuoteComponent {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  readonly isAuthenticated = this.auth.isAuthenticated;

  author = signal('');
  text = signal('');
  errorMessage = signal<string | null>(null);

  // Emits the created Quote (with the real id/createdAtUtc/ownerId the API
  // assigned) so the parent can append it without a refetch.
  quoteAdded = output<Quote>();

  onAuthorInput(event: Event): void {
    this.author.set((event.target as HTMLInputElement).value);
  }

  onTextInput(event: Event): void {
    this.text.set((event.target as HTMLInputElement).value);
  }

  onSubmit(): void {
    this.errorMessage.set(null);
    const request: CreateQuoteRequest = { author: this.author(), text: this.text() };

    this.http.post<Quote>(QUOTES_URL, request).subscribe({
      next: (quote) => {
        this.quoteAdded.emit(quote);
        this.author.set('');
        this.text.set('');
      },
      error: () => this.errorMessage.set('Failed to add quote. Are you logged in?'),
    });
  }
}
