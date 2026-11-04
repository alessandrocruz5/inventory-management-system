// Keep in sync with Kernel.Problems.ProblemTypes (CLAUDE.md Hidden coupling).
export const PROBLEM_MESSAGES: Readonly<Record<string, string>> = {
  validation: 'Please check the highlighted fields.',
  'invalid-credentials':
    'Email or password is incorrect. After 5 failed attempts the account is locked for 15 minutes.',
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
