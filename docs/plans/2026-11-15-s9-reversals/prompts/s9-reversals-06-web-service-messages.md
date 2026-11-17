---
type: prompt
plan: "[[2026-11-15-s9-reversals]]"
id: 06
unit: IMS-13
status: todo
model: haiku
effort: low
depends_on: [02]
parallel_group: D
files:
  create: []
  modify:
    - web/src/app/core/problems/problem-messages.ts
    - web/src/app/core/problems/problem-messages.spec.ts
    - web/src/app/documents/documents.service.ts
    - web/src/app/documents/documents.service.spec.ts
  delete: []
---
# 06 · Web: problem messages and `reverse()`

## Task
Map the three reversal problem slugs to friendly messages, and add `DocumentsService.reverse(id, idempotencyKey)`. Cover both with Vitest.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-13`, Angular app in `web/`. Hidden coupling (CLAUDE.md): `PROBLEM_MESSAGES` mirrors Kernel `ProblemTypes`; prompt 02 added `reversal-not-latest`, `already-reversed` and `cannot-reverse-reversal` there.
- **`web/src/app/core/problems/problem-messages.ts`** — add after `'inactive-location'` (`:15`), plain words for a shop owner (spec §10):
  ```ts
  'reversal-not-latest':
    'Stock of this item has moved since this document, so it can no longer be reversed. Post a correcting document instead.',
  'already-reversed': 'This document has already been reversed.',
  'cannot-reverse-reversal': 'A reversal cannot be reversed. Post a new document instead.',
  ```
- **`problem-messages.spec.ts`** — in `describe('problemMessage')`, add `it('maps the reversal slugs', …)` asserting each of the three types maps to its `PROBLEM_MESSAGES` entry (copy `:36-40`).
- **`web/src/app/documents/documents.service.ts`** — after `post` (`:132-139`):
  ```ts
  /** POST /api/documents/{id}/reversal with an empty body (spec §9). A 200 replay and a 201 both resolve here. */
  reverse(id: string, idempotencyKey: string): Promise<PostedDocument> {
    return firstValueFrom(
      this.http.post<PostedDocument>(`/api/documents/${encodeURIComponent(id)}/reversal`, null, {
        headers: { 'Idempotency-Key': idempotencyKey },
      }),
    );
  }
  ```
- **`documents.service.spec.ts`** — copy the `post sends … body and Idempotency-Key` test (`:95-104`) and `get encodes the id` (`:56`): `it('reverse posts an empty body with Idempotency-Key to the reversal route', …)`: call `reverse('a/b', 'k-1')`, expect one request to `/api/documents/a%2Fb/reversal`, method `POST`, body `null`, header `Idempotency-Key` = `k-1`; flush `{ id: 'r1', type: 'Reversal' }` and expect the resolved value.

## Do not
- Touch files outside the `files` list (the button is prompt 07).
- Add packages.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
git diff --stat        # only the four declared files
```
