---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 15
unit: U4
status: todo
model: sonnet
effort: medium
depends_on: [14]
parallel_group: O
files:
  create:
    - tests/e2e/specs/stocktake.spec.ts
  modify:
    - docs/Inventory System — Sprint 1 Spec.md
  delete: []
---
# 15 · Playwright stocktake flow; spec §10/§11/§15

## Task
Add one Playwright spec that proves the stocktake flow end to end at phone width, and record the screens and tests in the spec.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't.
- Read first:
  - `tests/e2e/support/app.ts`: `STAFF`, `ADMIN`, `signIn(page, email)`, and `createLocation(browser)`, which creates a fresh physical location as Admin through an in-browser `fetch` and returns `{ id, code }`.
  - `tests/e2e/specs/t14-stock-flow.spec.ts` and `post-stock-mobile.spec.ts`: the style, the 375 px viewport and how they receive stock.
  - `tests/e2e/playwright.config.ts`.
- TASKS.md IMS-9/IMS-16 Downstream:
  - Use `CANDLE-WHT` or your own item, because other seeded items carry stock.
  - Renaming seeded emails, port 4200 or UI labels used by specs breaks the suite.
  - The suite runs against the AppHost (or `E2E_BASE_URL`), with `E2E_PASSWORD` = the seed password.
- U4's UI labels (prompts 13–14): nav "Count stock", button "Start a count", row tap opens the count dialog, Admin "Post count", posted link "View the correction". Read the components to get the exact accessible names.

### Steps
1. `stocktake.spec.ts`, one `test`, viewport 375 × 812:
   1. As Admin, create a fresh location with `createLocation(browser)`, then receive 10 `CANDLE-WHT` @ ₱20 there. Reuse t14's receive steps, or an in-browser `fetch` to `/api/documents/receipts` with an `Idempotency-Key` from `crypto.randomUUID()`, matching `createLocation`'s technique.
   2. As Staff, go to "Count stock" → "Start a count", choose the new location, then open the count. The sheet lists `CANDLE-WHT` with expected 10. Count 8 and save. The row shows a difference of −2.
   3. Staff doesn't see "Post count".
   4. As Admin, open the same count and choose "Post count" → confirm. The status shows "Posted". "View the correction" opens a document whose type label is the adjustment's ("Correct stock" or as rendered) with the line −2.
   5. `/stock` filtered to the location shows `CANDLE-WHT` at 8 and ₱160.00.
2. Spec (`docs/Inventory System — Sprint 1 Spec.md`):
   - §10 screens table: rows for `/stocktakes` ("Count stock": list, filter, start) and `/stocktakes/:id` (the count sheet: expected, counted, difference, moved-since-count notice, units, Admin Post/Cancel, link to the correction). The Post-stock form row gains "unit per line".
   - §10 **Labels**: add "Count stock".
   - §11: add `T18 | Staff counts, Admin posts, stock shows the count (375 px) | Playwright | Stocktake end to end`.
   - §15 Log: "2026-11-18: Sprint 3 screens (§10) and T18."

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Change the existing e2e specs or the support helpers. If a helper is missing, inline it in the spec.
- Add npm packages. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
cd web && npm run lint && npm run build && npm test -- --watch=false && cd ..
cd tests/e2e && npx playwright test --list | grep -q stocktake
# with the stack running and E2E_PASSWORD set (README "End-to-end tests"):
cd tests/e2e && npx playwright test specs/stocktake.spec.ts
```
- The CI `e2e` job passes with the new spec.
