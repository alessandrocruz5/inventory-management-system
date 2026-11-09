---
type: prompt
plan: "[[2026-11-05-s5a-ledger-schema]]"
id: 04
unit: IMS-6
status: done
model: haiku
effort: low
depends_on: []
parallel_group: A
files:
  create: []
  modify:
    - web/src/app/core/problems/problem-messages.ts
    - web/src/app/core/problems/problem-messages.spec.ts
  delete: []
---
# 04 · Web: `stock-not-zero` message

## Task
Add the `stock-not-zero` slug to the Angular problem map and cover it in the spec. The API returns it as `422` when a deactivation is blocked by stock. Prompt 03 adds the same slug to Kernel's `ProblemTypes`; CLAUDE.md's Hidden coupling requires the two to match in the same PR.

## Context
- **Repo and branch:** the repo root is `/home/apa/Code/portfolio/inventory-management-system`, and you are on branch `IMS-6`. Don't switch it.
- **Stack:** Angular in `web/`, with Vitest through `npm test`.
- **`web/src/app/core/problems/problem-messages.ts`:** the `PROBLEM_MESSAGES` object (lines 2-8). Add one entry after `'concurrency-conflict'`, matching the existing quote and indent style:
  ```ts
  'stock-not-zero': 'This still has stock. Move, use or correct the stock to zero before deactivating it.',
  ```
  The Items and Locations screens already show `problemMessage(body)` on a failed deactivate, so nothing else changes.
- **`web/src/app/core/problems/problem-messages.spec.ts`:** in the `describe('problemMessage', …)` block (lines 36-43), add one `it`. Copy the existing `it` exactly in style:
  ```ts
  it('maps stock-not-zero', () => {
    expect(problemMessage({ type: '/problems/stock-not-zero', status: 422, balances: [] })).toBe(
      PROBLEM_MESSAGES['stock-not-zero'],
    );
  });
  ```

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Change `problemMessage`, `problemFieldErrors` or any other entry.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- The new spec case passes, and every existing web test is still green.
