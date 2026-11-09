import { PROBLEM_MESSAGES, problemFieldErrors, problemMessage } from './problem-messages';

describe('problemFieldErrors', () => {
  it('returns the errors of a validation body', () => {
    const body = {
      type: '/problems/validation',
      errors: { sku: ['Required.'], name: ['Too long.', 'Bad.'] },
    };
    expect(problemFieldErrors(body)).toEqual({ sku: ['Required.'], name: ['Too long.', 'Bad.'] });
  });

  it('returns {} for a non-validation type', () => {
    expect(problemFieldErrors({ type: '/problems/not-found', errors: { sku: ['x'] } })).toEqual({});
  });

  it('returns {} for null or a string', () => {
    expect(problemFieldErrors(null)).toEqual({});
    expect(problemFieldErrors('boom')).toEqual({});
  });

  it('returns {} when errors is missing or not an object', () => {
    expect(problemFieldErrors({ type: '/problems/validation' })).toEqual({});
    expect(problemFieldErrors({ type: '/problems/validation', errors: 'x' })).toEqual({});
  });

  it('drops values that are not arrays of strings', () => {
    const body = {
      type: '/problems/validation',
      errors: { sku: ['ok'], name: 'nope', baseUnit: [1, 2], version: [{}], other: null },
    };
    expect(problemFieldErrors(body)).toEqual({ sku: ['ok'] });
  });
});

describe('problemMessage', () => {
  it('maps not-found and concurrency-conflict', () => {
    expect(problemMessage({ type: '/problems/not-found' })).toBe(PROBLEM_MESSAGES['not-found']);
    expect(problemMessage({ type: '/problems/concurrency-conflict' })).toBe(
      PROBLEM_MESSAGES['concurrency-conflict'],
    );
  });

  it('maps stock-not-zero', () => {
    expect(problemMessage({ type: '/problems/stock-not-zero', status: 422, balances: [] })).toBe(
      PROBLEM_MESSAGES['stock-not-zero'],
    );
  });
});
