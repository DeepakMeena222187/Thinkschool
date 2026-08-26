import { HttpErrorResponse } from '@angular/common/http';
import { mapHttpErrorToAppError } from './app-error';

describe('mapHttpErrorToAppError', () => {
  it('maps the real 400 ValidationProblemDetails shape to a validation AppError', () => {
    // Same fixture confirmed via curl and pinned in quote.service.spec.ts.
    const err = new HttpErrorResponse({
      status: 400,
      statusText: 'Bad Request',
      url: 'http://localhost:5041/api/quotes',
      error: {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { Author: ['The Author field is required.'] },
        traceId: '00-c66a70b55009410ea7033f6bd8908f10-850a00f1650db0b9-01',
      },
    });

    const result = mapHttpErrorToAppError(err);

    expect(result).toEqual({
      kind: 'validation',
      fieldErrors: { Author: ['The Author field is required.'] },
      friendlyMessage: 'The Author field is required.',
    });
  });

  it('flattens multiple field errors across multiple fields into one message', () => {
    const err = new HttpErrorResponse({
      status: 400,
      error: {
        errors: {
          Author: ['The Author field is required.'],
          Text: ['The Text field is required.', 'Text must be 1000 characters or fewer.'],
        },
      },
    });

    const result = mapHttpErrorToAppError(err);

    expect(result.kind).toBe('validation');
    expect(result.friendlyMessage).toBe(
      'The Author field is required. The Text field is required. Text must be 1000 characters or fewer.',
    );
  });

  it('maps a 401 to an unauthorized AppError', () => {
    const err = new HttpErrorResponse({ status: 401 });

    expect(mapHttpErrorToAppError(err)).toEqual({
      kind: 'unauthorized',
      friendlyMessage: 'Your session has expired. Please log in again.',
    });
  });

  it('maps a 500 with no ProblemDetails body to a generic http AppError', () => {
    const err = new HttpErrorResponse({ status: 500 });

    expect(mapHttpErrorToAppError(err)).toEqual({
      kind: 'http',
      status: 500,
      friendlyMessage: 'Something went wrong. Please try again.',
    });
  });

  it('prefers the ProblemDetails "detail" field when present on a non-validation error', () => {
    const err = new HttpErrorResponse({
      status: 404,
      error: { detail: 'Quote 999 was not found.' },
    });

    expect(mapHttpErrorToAppError(err)).toEqual({
      kind: 'http',
      status: 404,
      friendlyMessage: 'Quote 999 was not found.',
    });
  });

  it('uses a call site-provided fallbackMessage over the generic default when there is no "detail"', () => {
    const err = new HttpErrorResponse({ status: 500 });

    const result = mapHttpErrorToAppError(err, {
      fallbackMessage: 'Failed to create the quote. Please try again.',
    });

    expect(result).toEqual({
      kind: 'http',
      status: 500,
      friendlyMessage: 'Failed to create the quote. Please try again.',
    });
  });

  it('still prefers a ProblemDetails "detail" over a call site fallbackMessage', () => {
    const err = new HttpErrorResponse({
      status: 404,
      error: { detail: 'Quote 999 was not found.' },
    });

    const result = mapHttpErrorToAppError(err, { fallbackMessage: 'Should not be used.' });

    expect(result.friendlyMessage).toBe('Quote 999 was not found.');
  });
});
