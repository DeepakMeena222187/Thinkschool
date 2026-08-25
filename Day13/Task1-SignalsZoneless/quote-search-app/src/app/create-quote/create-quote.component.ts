import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../auth/auth.service';
import { QuoteService } from '../quote-list-detail/quote.service';
import { CreateQuoteRequest, Quote } from '../models/quote.models';

// Angular's Validators.required treats a whitespace-only string as
// non-empty, but the server's [Required] attribute trims first (confirmed
// via curl: POST with author:"   " -> 400 "The Author field is required.").
// This mirrors that exact behavior instead of the looser built-in check.
function requiredNotBlank(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim().length > 0 ? null : { required: true };
}

// Shape returned by ASP.NET's Results.ValidationProblem() - confirmed via a
// real invalid POST against the running API rather than assumed: e.g.
// {"type":"...","title":"One or more validation errors occurred.",
//  "status":400,"errors":{"Author":["The Author field is required."]},"traceId":"..."}
// Field names in `errors` are PascalCase, matching the C# property names.
interface ValidationProblemDetails {
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

@Component({
  selector: 'app-create-quote',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './create-quote.component.html',
  styleUrl: './create-quote.component.css',
})
export class CreateQuoteComponent {
  private readonly auth = inject(AuthService);
  private readonly quoteService = inject(QuoteService);

  readonly isAuthenticated = this.auth.isAuthenticated;

  // Matches CreateQuoteRequest's server-side DataAnnotations exactly
  // (QuotesApi/Contracts/CreateQuoteRequest.cs): both required, Author capped
  // at 100 chars, Text capped at 1000.
  protected readonly authorMaxLength = 100;
  protected readonly textMaxLength = 1000;

  readonly form = new FormGroup({
    author: new FormControl('', {
      nonNullable: true,
      validators: [requiredNotBlank, Validators.maxLength(this.authorMaxLength)],
    }),
    text: new FormControl('', {
      nonNullable: true,
      validators: [requiredNotBlank, Validators.maxLength(this.textMaxLength)],
    }),
  });

  protected get authorControl() {
    return this.form.controls.author;
  }

  protected get textControl() {
    return this.form.controls.text;
  }

  private readonly authorInput = viewChild<ElementRef<HTMLInputElement>>('authorInput');
  private readonly textInput = viewChild<ElementRef<HTMLTextAreaElement>>('textInput');

  // Flips true after the first failed submit attempt, so field errors show
  // even for a control the user never individually blurred (e.g. they typed
  // in author then hit submit without ever touching text).
  readonly submitAttempted = signal(false);
  readonly submitting = signal(false);
  readonly successMessage = signal<string | null>(null);
  readonly serverError = signal<string | null>(null);

  // Lets a parent append the new quote to an already-loaded list instead of
  // refetching, the same convention AddQuoteComponent's quoteAdded uses.
  quoteCreated = output<Quote>();

  protected showError(control: FormControl<string>): boolean {
    return control.invalid && (control.touched || this.submitAttempted());
  }

  protected fieldErrorMessage(control: FormControl<string>, label: string, maxLength: number): string {
    if (control.hasError('required')) {
      return `${label} is required.`;
    }
    if (control.hasError('maxlength')) {
      const actualLength = (control.getError('maxlength') as { actualLength: number }).actualLength;
      return `${label} must be ${maxLength} characters or fewer (currently ${actualLength}).`;
    }
    return '';
  }

  onSubmit(): void {
    if (this.submitting()) {
      return;
    }

    this.serverError.set(null);
    this.successMessage.set(null);

    if (this.form.invalid) {
      this.submitAttempted.set(true);
      this.form.markAllAsTouched();
      this.focusFirstInvalidField();
      return;
    }

    this.submitting.set(true);
    const request: CreateQuoteRequest = this.form.getRawValue();

    this.quoteService.createQuote(request).subscribe({
      next: (quote) => {
        this.submitting.set(false);
        this.submitAttempted.set(false);
        this.successMessage.set(`Quote added: "${quote.text}" — ${quote.author}`);
        this.quoteCreated.emit(quote);
        this.form.reset({ author: '', text: '' });
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.serverError.set(this.describeServerError(err));
      },
    });
  }

  private focusFirstInvalidField(): void {
    if (this.authorControl.invalid) {
      this.authorInput()?.nativeElement.focus();
    } else if (this.textControl.invalid) {
      this.textInput()?.nativeElement.focus();
    }
  }

  private describeServerError(err: HttpErrorResponse): string {
    if (err.status === 401) {
      return 'Your session has expired. Please log in again.';
    }

    const problem = err.error as ValidationProblemDetails | null;
    if (err.status === 400 && problem?.errors) {
      return Object.values(problem.errors).flat().join(' ');
    }
    if (problem?.detail) {
      return problem.detail;
    }
    return 'Failed to create the quote. Please try again.';
  }
}
