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

  it('maps negative-balances-exist', () => {
    expect(
      problemMessage({ type: '/problems/negative-balances-exist', status: 422, balances: [] }),
    ).toBe(PROBLEM_MESSAGES['negative-balances-exist']);
  });

  it('maps the reversal slugs', () => {
    for (const slug of ['reversal-not-latest', 'already-reversed', 'cannot-reverse-reversal']) {
      expect(problemMessage({ type: `/problems/${slug}` })).toBe(PROBLEM_MESSAGES[slug]);
    }
  });

  it('maps the stocktake slugs', () => {
    expect(
      problemMessage({ type: '/problems/stocktake-open-exists', status: 409, stocktakeId: 'x' }),
    ).toBe(PROBLEM_MESSAGES['stocktake-open-exists']);
    expect(
      problemMessage({
        type: '/problems/stocktake-not-open',
        status: 409,
        stocktakeStatus: 'Posted',
      }),
    ).toBe(PROBLEM_MESSAGES['stocktake-not-open']);
  });

  it.each([
    'idempotency-key-missing',
    'idempotency-key-reused',
    'insufficient-stock',
    'inactive-item',
    'inactive-location',
  ])('maps %s', (slug) => {
    expect(problemMessage({ type: `/problems/${slug}` })).toBe(PROBLEM_MESSAGES[slug]);
  });
});
