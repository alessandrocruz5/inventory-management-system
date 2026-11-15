---
type: prompt
plan: "[[2026-11-15-s11-ledger-health]]"
id: 05
unit: IMS-15
status: todo
model: sonnet
effort: medium
depends_on: []
parallel_group: A
files:
  create:
    - web/src/app/ledger/ledger.service.ts
    - web/src/app/ledger/ledger.service.spec.ts
    - web/src/app/core/auth/admin.guard.ts
    - web/src/app/core/auth/admin.guard.spec.ts
  modify:
    - web/src/app/app.html
    - web/src/app/app.spec.ts
  delete: []
---
# 05 · Web: ledger service, Admin guard and nav link

## Task
Add the Angular plumbing for the Ledger health screen:
- a `LedgerService` for `GET /api/ledger/health` and `POST /api/ledger/rebuild`;
- a reusable functional `adminGuard`;
- an Admin-only "Ledger health" toolbar link;
- tests for each.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-15`. Stop if `git branch --show-current` says otherwise. Angular 22: standalone, signals, zoneless, `@if`/`@for`, functional guards. Tests are Vitest via `ng test`. No new npm packages.
- Spec §9 `:549-550` (both routes Admin), §10 `:606` (`/admin/ledger` · Ledger health · Admin).
- **API contract** (plan; JSON camelCase, decimals as numbers):
  ```ts
  export type InvariantId = 'INV-1' | 'INV-2' | 'INV-3' | 'INV-4' | 'INV-5';
  export interface InvariantFailure { itemId: string | null; sku: string | null; locationId: string | null; locationCode: string | null;
    movementId: number | null; check: string; expected: number | null; actual: number | null; }
  export interface InvariantResult { id: InvariantId; name: string; passed: boolean; failureCount: number; failures: InvariantFailure[]; }
  export interface LedgerHealth { checkedAt: string; healthy: boolean; invariants: InvariantResult[]; }
  export interface BalanceSnapshot { onHand: number; stockValue: number; lastUnitCost: number | null; lastMovementId: number | null; }
  export interface BalanceDifference { itemId: string; sku: string; locationId: string; locationCode: string; inserted: boolean;
    before: BalanceSnapshot | null; after: BalanceSnapshot; }
  export interface LedgerRebuild { rebuiltAt: string; balancesChecked: number; differences: BalanceDifference[]; }
  ```
- Patterns: `web/src/app/stock/stock.service.ts` (`@Injectable({ providedIn: 'root' })`, `inject(HttpClient)`, `firstValueFrom`); `web/src/app/documents/documents.service.spec.ts` (`provideHttpClient()`, `provideHttpClientTesting()`, `httpMock.expectOne`, `afterEach(() => httpMock.verify())`); `web/src/app/documents/post-stock.guard.ts` and `post-stock.guard.spec.ts` (functional guard on `AuthService.ensureLoaded()` returning a `UrlTree` to `/`); `web/src/app/app.html` (the Admin-only "Correct stock" link inside `@if (auth.isAdmin())`); `web/src/app/app.spec.ts:81-92` (the Admin-only link test).

### 1. `ledger/ledger.service.ts`
The types above, then `LedgerService` with `health(): Promise<LedgerHealth>` (GET `/api/ledger/health`) and `rebuild(): Promise<LedgerRebuild>` (POST `/api/ledger/rebuild` with body `null`; no `Idempotency-Key`).

### 2. `core/auth/admin.guard.ts`
`export const adminGuard: CanActivateFn = async () => { const auth = inject(AuthService); const router = inject(Router); return (await auth.ensureLoaded())?.role === 'Admin' ? true : router.createUrlTree(['/']); };`. Both `inject` calls come before the `await`, as in `post-stock.guard.ts`: `inject` is invalid after an await.

### 3. Nav
- The route itself is prompt 06's, together with the component it loads, so each prompt builds on its own. Until then the link falls through to `**`.
- `app.html`: after the Locations link, `@if (auth.isAdmin()) { <a mat-button routerLink="/admin/ledger" routerLinkActive="active">Ledger health</a> }`.

### 4. Tests
- `ledger.service.spec.ts`: `health()` sends `GET /api/ledger/health` and resolves the flushed body; `rebuild()` sends `POST /api/ledger/rebuild` with a `null` body and no `Idempotency-Key` header.
- `admin.guard.spec.ts`: Admin → `true`; Staff → a `UrlTree` for `/`; signed out (`ensureLoaded` → null) → a `UrlTree` for `/`.
- `app.spec.ts`: add `it('shows Ledger health to Admin only', …)` mirroring `:81-92` with `a[href="/admin/ledger"]` and the text "Ledger health".

## Do not
- Touch files outside `files`; `app.routes.ts` is prompt 06's. If you must, stop and report why.
- Add npm packages, an NgRx store or a new interceptor; change `PROBLEM_MESSAGES` (no new slugs in S11).
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
```
- Staff sees no "Ledger health" link, and `adminGuard` redirects Staff to `/`.
