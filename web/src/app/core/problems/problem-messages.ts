// Keep in sync with Kernel.Problems.ProblemTypes (CLAUDE.md Hidden coupling).
export const PROBLEM_MESSAGES: Readonly<Record<string, string>> = {
  validation: 'Please check the highlighted fields.',
  'invalid-credentials':
    'Email or password is incorrect. After 5 failed attempts the account is locked for 15 minutes.',
  'not-found': "That record doesn't exist, or you can't see it.",
  'concurrency-conflict': 'Someone else changed this record. Reload it and try again.',
  'stock-not-zero':
    'This still has stock. Move, use or correct the stock to zero before deactivating it.',
  'idempotency-key-missing': 'Something went wrong sending this. Please try again.',
  'idempotency-key-reused':
    'This form was already used for a different posting. Open the original or start a new form.',
  'insufficient-stock': 'There is not enough stock for one of the lines.',
  'inactive-item': 'One of the items is no longer active.',
  'inactive-location': 'This location is no longer active.',
};

export const DEFAULT_PROBLEM_MESSAGE = 'Something went wrong. Please try again.';

const PROBLEM_PREFIX = '/problems/';

export function problemSlug(type: unknown): string | null {
  if (typeof type !== 'string' || !type.startsWith(PROBLEM_PREFIX)) {
    return null;
  }
  const slug = type.slice(PROBLEM_PREFIX.length);
  return slug.length > 0 ? slug : null;
}

export function problemMessage(body: unknown): string {
  if (typeof body !== 'object' || body === null) {
    return DEFAULT_PROBLEM_MESSAGE;
  }
  const slug = problemSlug((body as { type?: unknown }).type);
  if (slug !== null && Object.hasOwn(PROBLEM_MESSAGES, slug)) {
    return PROBLEM_MESSAGES[slug];
  }
  return DEFAULT_PROBLEM_MESSAGE;
}

/** The per-field messages of a `validation` problem, keyed as the API sends them; empty for any other body. */
export function problemFieldErrors(body: unknown): Readonly<Record<string, string[]>> {
  if (typeof body !== 'object' || body === null) {
    return {};
  }
  const { type, errors } = body as { type?: unknown; errors?: unknown };
  if (problemSlug(type) !== 'validation' || typeof errors !== 'object' || errors === null) {
    return {};
  }
  const result: Record<string, string[]> = {};
  for (const [key, value] of Object.entries(errors)) {
    if (Array.isArray(value) && value.every((m) => typeof m === 'string')) {
      result[key] = value;
    }
  }
  return result;
}
