---
type: prompt
plan: "[[2026-11-09-s5c-posting-engine]]"
id: 02
unit: IMS-8
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
    - "docs/Inventory System — Sprint 1 Spec.md"
  delete: []
---
# 02 · Web slugs and spec §6 amendment

## Task
Add five ProblemDetails slugs to the Angular message map, with one test, and amend spec §6 with two decisions the user approved. These are text edits only.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-8`. Don't switch it.
- The map must match `Kernel.Problems.ProblemTypes` (CLAUDE.md Hidden coupling). Prompt 03 adds the same five constants in C#.

**1. `web/src/app/core/problems/problem-messages.ts`.** After the `'stock-not-zero'` entry (line 8-9), add these lines exactly, keeping the existing formatting (Prettier, single quotes):
```ts
  'idempotency-key-missing': 'Something went wrong sending this. Please try again.',
  'idempotency-key-reused':
    'This form was already used for a different posting. Open the original or start a new form.',
  'insufficient-stock': 'There is not enough stock for one of the lines.',
  'inactive-item': 'One of the items is no longer active.',
  'inactive-location': 'This location is no longer active.',
```

**2. `web/src/app/core/problems/problem-messages.spec.ts`.** Inside `describe('problemMessage', …)`, after the `'maps stock-not-zero'` test, add:
```ts
  it.each([
    'idempotency-key-missing',
    'idempotency-key-reused',
    'insufficient-stock',
    'inactive-item',
    'inactive-location',
  ])('maps %s', (slug) => {
    expect(problemMessage({ type: `/problems/${slug}` })).toBe(PROBLEM_MESSAGES[slug]);
  });
```
If `it.each` fails lint or the type check, write five plain `it(...)` cases instead.

**3. `docs/Inventory System — Sprint 1 Spec.md`.** Make these edits in §6, "Posting steps":
- At the end of step 1 (the line starting `1. Validate the request:`), append this sentence:
  `A line whose Round4(quantity × unitCost) exceeds 999,999,999,999,999.9999 (the decimal(19,4) maximum) fails on lines[i].unitCost. The checks that items and locations exist and are active run after step 2, so a replay of a successful post returns the original even if master data has changed since.`
- At the end of step 2 (the line starting `2. Idempotency fast path:`), append:
  `The hash covers the validated request, so the lookup runs right after the pure checks of step 1.`
- At the end of step 8 (the line starting `8. Compute each line's cost`), append:
  `Reject with 400 validation on lines[i].quantity if any new OnHand or StockValue would fall outside decimal(19,4).`
- In §15 Log, after the last bullet, add:
  `- 2026-11-09: S5c plan amended §6 steps 1, 2 and 8 (overflow guards; replay before the database checks of step 1).`

Change nothing else in the spec.

## Do not
- Touch files outside the `files` list.
- Change any existing map entry or test.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
cd .. && git diff --stat -- "docs/Inventory System — Sprint 1 Spec.md"   # 1 file, about 4 lines changed
```
- The five new cases pass, and the existing web tests still pass.
