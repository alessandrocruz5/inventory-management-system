---
type: prompt
plan: "[[2026-11-15-s11-ledger-health]]"
id: 06
unit: IMS-15
status: done
model: sonnet
effort: high
depends_on: [05]
parallel_group: B
files:
  create:
    - web/src/app/ledger/ledger-health.ts
    - web/src/app/ledger/ledger-health.html
    - web/src/app/ledger/ledger-health.scss
    - web/src/app/ledger/ledger-health.spec.ts
  modify:
    - web/src/app/app.routes.ts
  delete: []
---
# 06 · Web: the Ledger health screen

## Task
Build `/admin/ledger` for Admins. It shows INV-1 to INV-5 pass/fail with their failing rows and a "Check again" button. A Rebuild button asks for confirmation, then shows the returned differences (or "nothing to fix") and re-checks health. Register the route behind `adminGuard`.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-15`. Stop if `git branch --show-current` says otherwise. Angular 22: standalone, signals, zoneless, `@if`/`@for`. Angular Material is installed. No new npm packages. Vitest via `ng test`.
- Spec §10 `:606`: *"`/admin/ledger` · Ledger health · Admin · INV-1 to INV-5 pass/fail with failing rows; Rebuild button with a confirm step and the returned diff"*. The screens are written for a shop owner who isn't technical: plain words, ₱ amounts. Money shows via `formatMoney` and quantities via `formatQuantity` (`web/src/app/shared/format.ts`).
- Prompt 05 added `web/src/app/ledger/ledger.service.ts` (`LedgerService.health()`, `.rebuild()`, plus the `LedgerHealth`, `InvariantResult`, `InvariantFailure`, `LedgerRebuild`, `BalanceDifference` and `BalanceSnapshot` types) and `web/src/app/core/auth/admin.guard.ts` (`adminGuard`). Read both first.
- Patterns: `web/src/app/documents/document-detail.ts:131-191` (confirm via `MatDialog.open(ConfirmDialog, …)` → call → `HttpErrorResponse` → `problemMessage(body)`; a busy signal disables the button); `web/src/app/shared/confirm-dialog.ts`; `web/src/app/items/item-detail.{ts,html}` (plain `<table>` styling, loading and error states); `web/src/app/items/item-detail.spec.ts` (component test setup: providers, `HttpTestingController` or a stubbed service, `fixture.whenStable()`). Check whether `item-detail` uses a `.scss`, and match its styling approach.

### 1. Route (`app.routes.ts`)
Before `**`: `{ path: 'admin/ledger', canActivate: [authGuard, adminGuard], loadComponent: () => import('./ledger/ledger-health').then((m) => m.LedgerHealth) }`.

### 2. Component `LedgerHealth` (`ledger-health.ts`, `.html`, `.scss`) (plan D11)
- **Signals:** `health` (`LedgerHealth | null`), `loading`, `rebuilding`, `error` (`string | null`), and `rebuild` (`LedgerRebuild | null`, the last result). Load health on init.
- **Header:** the title "Ledger health", "Checked {Manila date-time}" (`formatManilaDateTime`), a "Check again" button, and a "Rebuild balances" button. Disable both while `loading()` or `rebuilding()`.
- **Overall line:** "All checks passed" or "{n} checks failed".
- **One block per invariant, in API order:**
  - The id plus a plain label. INV-1 "Balances match the ledger". INV-2 "Stock adds up across locations". INV-3 "No impossible balances". INV-4 "Reversals mirror their originals". INV-5 "Last unit cost matches the ledger".
  - A Passed/Failed marker (text plus a class, not colour alone).
  - When it failed: "{failureCount} problems", plus "showing the first 100" when `failureCount > failures.length`, and a table of `failures`: SKU, Location, Movement, Check, Expected, Actual. Money checks (`StockValue`, `TotalCost`, `LastUnitCost`) use `formatMoney`; quantity checks (`OnHand`, `OnHandSum`, `Quantity`) use `formatQuantity`; others show the raw number or "—" for null.
  - Wrap each table in a container with `overflow-x: auto` so it scrolls inside the page at 375 px.
- **Rebuild:** open `ConfirmDialog` with title "Rebuild balances", the message "Recalculate every stock balance from the ledger and fix any that differ? Stock postings wait until it finishes. The ledger itself is never changed.", and confirm "Rebuild". Cancel does nothing.
  - On confirm, set `rebuilding`, call `rebuild()`, store the result, then reload health.
  - Show the result: "Checked {balancesChecked} balances. Nothing to fix." when `differences` is empty. Otherwise "Fixed {n} balances" and a table: SKU, Location, then Before → After for on hand (`formatQuantity`), value (`formatMoney`), last unit cost (`formatMoney` or "—") and last movement, with "(row was missing)" when `inserted`.
- **Errors:** an `HttpErrorResponse` sets `error` to `problemMessage(body)` (import from `core/problems/problem-messages`), shown in an alert area. A `409 concurrency-conflict` after a rebuild therefore reads as the existing mapped message.

### 3. `ledger-health.spec.ts`
Stub `LedgerService` (or use `HttpTestingController`) and `MatDialog`.
- Renders five invariant blocks with their labels. A healthy response shows "All checks passed" and no failure tables.
- A failed INV-1 with `failureCount: 150` and two `failures` shows "150 problems", "showing the first 100", and two rows with SKU, location and formatted expected/actual.
- Rebuild, dialog cancelled → `rebuild()` not called.
- Rebuild, dialog confirmed, empty differences → "Nothing to fix" with `balancesChecked`, and `health()` is called a second time.
- Rebuild with one difference where `inserted: true` → the row shows "(row was missing)" and the "after" values.
- `health()` rejects with a ProblemDetails `HttpErrorResponse` → the mapped message is shown.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Add npm packages; use AG Grid here (plain tables, plan D11); change `PROBLEM_MESSAGES` or the service contract.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
```
- As Admin, `/admin/ledger` lists the five checks; as Staff, `/admin/ledger` redirects to `/` (guard from prompt 05).
