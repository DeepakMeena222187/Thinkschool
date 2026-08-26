import { HttpErrorResponse } from '@angular/common/http';

// Shape returned by ASP.NET's Results.ValidationProblem() - confirmed via a
// real invalid POST against the running API rather than assumed: e.g.
// {"type":"...","title":"One or more validation errors occurred.",
//  "status":400,"errors":{"Author":["The Author field is required."]},"traceId":"..."}
// Field names in `errors` are PascalCase, matching the C# property names.
export interface ValidationProblemDetails {
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

// Typed shape the UI can branch on, instead of every component re-deriving
// a message from a raw HttpErrorResponse.
export type AppError =
  | { kind: 'validation'; fieldErrors: Record<string, string[]>; friendlyMessage: string }
  | { kind: 'unauthorized'; friendlyMessage: string }
  | { kind: 'http'; status: number; friendlyMessage: string };

export function flattenFieldErrors(fieldErrors: Record<string, string[]>): string {
  return Object.values(fieldErrors).flat().join(' ');
}

// `fallbackMessage` lets a call site keep its own wording for the generic
// case (no ProblemDetails `detail`, not a 401, not a validation error)
// instead of silently taking this layer's generic default.
export function mapHttpErrorToAppError(
  err: HttpErrorResponse,
  options?: { fallbackMessage?: string },
): AppError {
  if (err.status === 401) {
    return { kind: 'unauthorized', friendlyMessage: 'Your session has expired. Please log in again.' };
  }

  const problem = err.error as ValidationProblemDetails | null;
  if (err.status === 400 && problem?.errors) {
    return {
      kind: 'validation',
      fieldErrors: problem.errors,
      friendlyMessage: flattenFieldErrors(problem.errors),
    };
  }

  return {
    kind: 'http',
    status: err.status,
    friendlyMessage: problem?.detail ?? options?.fallbackMessage ?? 'Something went wrong. Please try again.',
  };
}
