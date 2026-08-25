import { Component, inject, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  FieldTree,
  FormField,
  FormRoot,
  TreeValidationResult,
  form,
  maxLengthError,
  requiredError,
  validate,
} from '@angular/forms/signals';
import { AuthService } from '../auth/auth.service';
import { QuoteService } from '../quote-list-detail/quote.service';
import { CreateQuoteRequest, Quote } from '../models/quote.models';

// Same shape confirmed via curl in the reactive-forms version (real POST
// /api/quotes 400s): {type,title,status,errors:{Author:[...],Text:[...]},traceId}
interface ValidationProblemDetails {
  detail?: string;
  errors?: Record<string, string[]>;
}

@Component({
  selector: 'app-create-quote-signal',
  standalone: true,
  imports: [FormRoot, FormField],
  templateUrl: './create-quote-signal.component.html',
  styleUrl: './create-quote-signal.component.css',
})
export class CreateQuoteSignalComponent {
  private readonly auth = inject(AuthService);
  private readonly quoteService = inject(QuoteService);

  readonly isAuthenticated = this.auth.isAuthenticated;

  protected readonly authorMaxLength = 100;
  protected readonly textMaxLength = 1000;

  // form() wraps this signal directly - no separate FormGroup copy the way
  // reactive forms needs one. The signal IS the model; the field tree is a
  // live, structured view over it.
  private readonly model = signal<CreateQuoteRequest>({ author: '', text: '' });

  readonly quoteForm: FieldTree<CreateQuoteRequest> = form(
    this.model,
    (p) => {
      // Same real limits as CreateQuoteRequest.cs's DataAnnotations
      // (StringLength(100/1000, MinimumLength=1)) - matches the
      // reactive-forms version exactly, not invented.
      //
      // Signal Forms' built-in required() doesn't document trim behavior,
      // and the server's [Required] trims first (confirmed via curl:
      // author:"   " -> 400 "The Author field is required."), so this uses
      // the same custom whitespace-aware check as requiredNotBlank() in the
      // reactive-forms version instead of trusting required() to match.
      //
      // Deliberately NOT using the built-in maxLength() here. Verified live
      // (Playwright `fill()`, which mimics real typing/paste) that maxLength()
      // also binds FieldState.maxLength to the native <input maxlength> DOM
      // attribute, which makes the browser silently cap real typing/pasting
      // at the limit - a raw script `el.value = ` assignment can still slip
      // past it, but no real user typing or pasting can. That makes the
      // "too long" error below unreachable through normal interaction, unlike
      // the reactive-forms version where Validators.maxLength(100) sets no
      // native attribute and a user actually can type past it and see the
      // error. validate() does not touch FieldState.maxLength, so it
      // reproduces the reactive-forms behavior exactly instead.
      validate(p.author, ({ value }) => {
        const current = value();
        if (current.trim().length === 0) {
          return requiredError({ message: 'Author is required.' });
        }
        if (current.length > this.authorMaxLength) {
          return maxLengthError(this.authorMaxLength, {
            message: `Author must be ${this.authorMaxLength} characters or fewer (currently ${current.length}).`,
          });
        }
        return undefined;
      });

      validate(p.text, ({ value }) => {
        const current = value();
        if (current.trim().length === 0) {
          return requiredError({ message: 'Quote text is required.' });
        }
        if (current.length > this.textMaxLength) {
          return maxLengthError(this.textMaxLength, {
            message: `Quote text must be ${this.textMaxLength} characters or fewer (currently ${current.length}).`,
          });
        }
        return undefined;
      });
    },
    {
      submission: {
        action: async (): Promise<TreeValidationResult> => {
          const value = this.quoteForm().value();
          try {
            const quote = await firstValueFrom(this.quoteService.createQuote(value));
            this.successMessage.set(`Quote added: "${quote.text}" — ${quote.author}`);
            this.quoteCreated.emit(quote);
            // reset() clears touched/dirty AND can set the value in one call,
            // unlike reactive forms' form.reset({...}) + no separate touched API.
            this.quoteForm().reset({ author: '', text: '' });
            return undefined;
          } catch (err) {
            return this.mapServerError(err);
          }
        },
        onInvalid: () => {
          // markAllAsTouched() has no direct Signal Forms equivalent on the
          // root FieldState, so each leaf is marked explicitly - verified
          // live that this is in fact necessary (submit() does not appear
          // to auto-touch untouched fields on a failed validation attempt).
          this.quoteForm.author().markAsTouched();
          this.quoteForm.text().markAsTouched();
          if (this.quoteForm.author().invalid()) {
            this.quoteForm.author().focusBoundControl();
          } else if (this.quoteForm.text().invalid()) {
            this.quoteForm.text().focusBoundControl();
          }
        },
      },
    },
  );

  readonly successMessage = signal<string | null>(null);

  // Same convention as the reactive-forms CreateQuoteComponent's
  // quoteCreated output - lets app.ts append to QuotesStore.
  quoteCreated = output<Quote>();

  private mapServerError(err: unknown): TreeValidationResult {
    if (!(err instanceof HttpErrorResponse)) {
      return { kind: 'server', message: 'Failed to create the quote. Please try again.' };
    }
    if (err.status === 401) {
      return { kind: 'server', message: 'Your session has expired. Please log in again.' };
    }

    const problem = err.error as ValidationProblemDetails | null;
    if (err.status === 400 && problem?.errors) {
      // Unlike the reactive-forms version's flat serverError banner, each
      // server field error is routed straight onto its real field via
      // fieldTree - it renders through the same errors() the client-side
      // validators use, not a separate error-display path.
      return Object.entries(problem.errors).flatMap(([field, messages]) => {
        const target: FieldTree<string> | undefined =
          field === 'Author' ? this.quoteForm.author : field === 'Text' ? this.quoteForm.text : undefined;
        return messages.map((message) => ({ kind: 'server', message, fieldTree: target }));
      });
    }

    return { kind: 'server', message: problem?.detail ?? 'Failed to create the quote. Please try again.' };
  }
}
