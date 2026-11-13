---
type: prompt
plan: "[[2026-11-13-s7-stock-screens]]"
id: 08
unit: IMS-11
status: todo
model: sonnet
effort: high
depends_on: [06, 07]
parallel_group: H
files:
  create:
    - web/src/app/documents/post-stock.ts
    - web/src/app/documents/post-stock.html
    - web/src/app/documents/post-stock.scss
    - web/src/app/documents/post-stock.spec.ts
    - web/src/app/documents/post-stock.guard.ts
    - web/src/app/documents/post-stock.guard.spec.ts
  modify: []
  delete: []
---
# 08 · Post-stock screen and guard

## Task
Create the standalone `PostStock` component: one form for all four posting types (spec §10 "Post-stock form"), wired to the pure logic of prompt 07. Also create `postStockGuard`. Prompt 10 routes them at `documents/new/:type`; don't touch the routes here.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-11`. Angular 22 standalone, signals, zoneless, `@if`/`@for`, **typed** reactive forms (`FormGroup`/`FormArray`/`FormControl` with `nonNullable` where it applies), Angular Material (form field, input, select, autocomplete, button, button toggle, snack bar, dialog). No new package.
- Typed form + server error pattern: `web/src/app/items/item-dialog.ts:42-95` (`problemFieldErrors`, `control.setErrors({ server: msg })`, `markAsTouched`, a `saving` signal, `error` signal). Confirm pattern: `web/src/app/items/items.ts:119-153` with `ConfirmDialog` from `web/src/app/shared/confirm-dialog.ts` (`{ title, message, confirm }` → `boolean`).
- Logic to use, not re-implement (prompt 07, `web/src/app/documents/posting-form.ts`): `PostingHeader`, `PostingLine`, `LineDirection`, `POSTING_TITLES`, `MAX_LINES`, `usesUnitCost`, `unitCostRequired`, `isOutflow`, `sourceLocationId`, `buildPostingBody`, `linesOverOnHand`, `duplicateItemLines`, `amountValidator`, `IdempotencyKey`, `postingErrors`.
- Services: `DocumentsService.post(type, key, body)` and `POSTING_TYPES` (`web/src/app/documents/documents.service.ts`), `StockService.onHand(itemId, locationId)` (`web/src/app/stock/stock.service.ts`), `ItemsService.list({ search, active: true, page: 1, pageSize: 10 })` (`web/src/app/items/items.service.ts`; `Item` has `id`, `sku`, `name`, `baseUnit`), `LocationsService.list({ page: 1, pageSize: 100 })` (keep `!isSystem && isActive`), `AuthService.isAdmin` (signal) and `ensureLoaded()` (`web/src/app/core/auth/auth.service.ts`). From `shared/format.ts`: `ADJUSTMENT_REASONS`, `reasonLabel`, `formatQuantity`.
- **Component behaviour:**
  - Read `:type` from `ActivatedRoute.paramMap` (subscribe with `takeUntilDestroyed()`). **On every emission** (plan D10; Angular reuses the component when only `:type` changes), reset the header, set the lines to one empty line, clear errors, set `type` and the title (`POSTING_TITLES`, also the `<h1>`), and create a **new** `IdempotencyKey`. The key is otherwise created once per form open (spec §10).
  - Header by type: `locationId` (receipt, issue, adjustment; label "Location"); `fromLocationId`/`toLocationId` (transfer; labels "From" and "To"); `reasonCode` (adjustment; `mat-select` over `ADJUSTMENT_REASONS` with `reasonLabel`); `reference` (max 100) and `notes` (max 500, textarea) on every type. Locations come from a `mat-select` of active physical locations, `CODE · Name`.
  - Lines (`FormArray`, 1..`MAX_LINES`; Add line / Remove line buttons; never remove the last one):
    - Item: `matInput` + `mat-autocomplete`. Typing (debounced 300 ms, distinct) calls `ItemsService.list({ search, active: true, page: 1, pageSize: 10 })`; options show `SKU · Name`; choosing one sets the line's `itemId` and remembers the item's `baseUnit`.
    - Adjustment only: a `mat-button-toggle-group` "Add" / "Remove" (`direction`, default `add`).
    - Quantity: `type="number"`, `Validators.required` + `amountValidator({ allowZero: false })`.
    - Unit cost: shown when `usesUnitCost(type, line)`. It's required when `unitCostRequired(type)` (receipt), and always has `amountValidator({ allowZero: true })`. Clear and hide it when the direction flips to Remove.
    - On-hand: when a line's item or the source location (`sourceLocationId`) changes and both are set, call `StockService.onHand` and show `On hand: {formatQuantity(n)} {unit}` beside the line. Keep the values in a signal array aligned with the lines. A failed read shows nothing (the server decides).
    - Show the duplicate-item error from `duplicateItemLines` on the line's item field before submit.
  - Submit (`type="submit"`; disabled while `posting()` or the form is invalid; this also blocks double clicks, spec §10):
    1. `form.markAllAsTouched()`. Stop if invalid or there are duplicates.
    2. `over = linesOverOnHand(type, lines, onHand())`. If it's non-empty, open `ConfirmDialog` with `{ title: 'Not enough stock?', message: 'Line N asks for X but only Y is on hand. Post anyway?' (one sentence per line), confirm: 'Post anyway' }`. Stop on cancel.
    3. `posting.set(true)`, then `DocumentsService.post(type, key.current, buildPostingBody(type, header, lines))`.
    4. Success (201 or 200 replay): snack bar `Posted`, then navigate to `['/documents', posted.id]`. The component is gone, so the next form gets a new key.
    5. Error: `const e = postingErrors(type, err instanceof HttpErrorResponse ? err.error : null, lines, units)`. Set `e.header` errors on header controls and `e.lines` on line controls as `{ server: msg }` + `markAsTouched()`. Show `e.message` in an error banner. If `e.originalDocumentId`, show a link `Open the original posting` → `/documents/<id>` (`routerLink`). If `e.renewKey`, call `key.renew()`. Otherwise **keep the same key** for the retry (spec §10).
    6. `finally` `posting.set(false)`.
  - Retrying after an error: a `server` error clears when its control's value changes (Angular re-runs the validators; never re-add it). An error with only `e.message` and no field errors leaves the form valid, so the same values can be resubmitted with the same key.
  - Each `mat-form-field` shows `<mat-error>` for `required`, `amount` (the validator's message) and `server`.
  - Layout: one column below 600 px (375 px must work, spec §10). Lines become cards stacking the fields. Keep `post-stock.scss` small (component style budget warns at 4 kB, `angular.json:37-47`).
- **`post-stock.guard.ts`**: `export const postStockGuard: CanActivateFn = async (route) => { … }`. Use `inject(AuthService)` and `inject(Router)`. Return `router.createUrlTree(['/'])` when `route.paramMap.get('type')` is not one of `POSTING_TYPES`, or when it's `'adjustment'` and `(await auth.ensureLoaded())?.role !== 'Admin'`; else `true`. Copy `web/src/app/core/auth/auth.guard.ts` for shape. Prompt 10 lists it after `authGuard`.
- **Specs** (T13's component half). Copy `web/src/app/items/items.spec.ts:1-45` for TestBed with `vi.fn()` service stubs. Provide `ActivatedRoute` with a `BehaviorSubject` `paramMap` (`convertToParamMap({ type: 'receipt' })`), `provideRouter([])`, and stubs for `DocumentsService`, `StockService`, `ItemsService`, `LocationsService`, `AuthService` (`isAdmin` signal, `ensureLoaded`), and `MatDialog` (`open` returns `{ afterClosed: () => of(true) }`) and `MatSnackBar`. Drive the form through the component's typed form (protected members may be reached with `component['form']`, which the repo's specs already do for protected handlers) rather than the autocomplete overlay. Facts:
  1. `idempotency key reuse`: a first `post` rejecting with `HttpErrorResponse({ status: 422, error: { type: '/problems/insufficient-stock', itemId, locationId, available: 2, requested: 5 } })`, and the line shows `Only 2 available; you asked for 5.`. Then set line 0's quantity to 2 (which clears its `server` error) and submit again: both calls carry the **same** key (audit FIX 4).
  2. After `idempotency-key-reused` (`409`, `originalDocumentId: 'd1'`): the next `post` uses a **different** key, and a link to `/documents/d1` is rendered.
  3. A network error (`status: 0`) keeps the key.
  4. Emitting `paramMap` `{ type: 'issue' }` resets the lines and changes the key and the title to `Use or sell stock`.
  5. Submit is disabled while a `post` promise is pending.
  6. Success navigates to `/documents/<id>` (spy on `Router.navigate`).
  7. Adjustment: switching a line to Remove hides the unit cost and posts a negative `quantityDelta` with `unitCost: null`.
  8. An outflow line over on-hand opens the confirm, and a `false` result posts nothing.
  9. A `400` with `errors: { 'lines[0].quantityDelta': ['…'] }` on an adjustment marks line 0's quantity control with `server`.
  - `post-stock.guard.spec.ts`: an unknown type → `UrlTree` to `/`; `adjustment` as Staff → `/`; `adjustment` as Admin → `true`; `receipt` as Staff → `true`. Use `TestBed.runInInjectionContext`.

## Do not
- Re-implement anything that `posting-form.ts` exports, or format numbers by hand.
- Generate a new key on a retry other than after `idempotency-key-reused`, a success, or a `:type` change.
- Add routes or navigation (prompt 10).
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- `post-stock.spec.ts` (9 facts) and `post-stock.guard.spec.ts` pass, and every existing spec passes unmodified.
