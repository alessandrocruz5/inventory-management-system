---
type: prompt
plan: "[[2026-11-05-s4-master-data]]"
id: 07
unit: IMS-5
status: todo
model: sonnet
effort: medium
depends_on: [02, 04]
parallel_group: G
files:
  create:
    - web/src/app/core/api/paged.ts
    - web/src/app/core/problems/problem-messages.spec.ts
    - web/src/app/items/items.service.ts
    - web/src/app/items/items.service.spec.ts
    - web/src/app/items/item-dialog.ts
    - web/src/app/items/item-dialog.html
    - web/src/app/items/item-dialog.spec.ts
  modify:
    - web/src/app/core/problems/problem-messages.ts
  delete: []
---
# 07 · Web: paged type, field errors, items service and dialog

## Task
Add the client side of the Items API: a `Paged<T>` type, a `problemFieldErrors` helper, an `ItemsService`, and the create/edit `ItemDialog`, each with Vitest specs. No route or page yet: prompt 08 adds the page and the grid.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`; the app is in `web/` (Angular 22, standalone, zoneless, signals, `@if`/`@for`, typed reactive forms, Angular Material 22, Vitest). You are on branch `IMS-5`; don't switch it. **Copy the patterns** from `web/src/app/login/login.ts` (typed `FormGroup`, `submitting`/`error` signals, `problemMessage(err.error)`), `login/login.spec.ts` (TestBed + `vi.fn()`) and `core/auth/auth.service.ts` + `.spec.ts` (`firstValueFrom`, `HttpTestingController`). Run Prettier on every file you touch (`npx prettier --write <files>`); lint is `ng lint` with angular-eslint.

**API contract** (prompts 03–04; JSON is camelCase):
- `GET /api/items?search=&active=&page=&pageSize=` → `{ items: Item[], page, pageSize, total }`
- `GET /api/items/{id}` → `Item`
- `POST /api/items` `{ sku, name, baseUnit }` → `201 Item` (Admin)
- `PUT /api/items/{id}` `{ sku, name, baseUnit, version }` → `200 Item`; a stale version → `409 /problems/concurrency-conflict`
- `POST /api/items/{id}/deactivate` (empty body) → `200 Item`
- `Item = { id: string; sku: string; name: string; baseUnit: string; isActive: boolean; version: string }` (`version` is base64; send it back unchanged)
- Validation → `400` `{ type: '/problems/validation', errors: { sku?: string[], name?: string[], baseUnit?: string[], version?: string[] } }`

**1. `core/api/paged.ts`:** `export interface Paged<T> { items: T[]; page: number; pageSize: number; total: number; }`, with a one-line comment: "Matches Inventory's `PagedResponse<T>` (spec §9 lists)."

**2. `core/problems/problem-messages.ts`.** Add, below `problemMessage`:
```ts
/** The per-field messages of a `validation` problem, keyed as the API sends them; empty for any other body. */
export function problemFieldErrors(body: unknown): Readonly<Record<string, string[]>>
```
It returns `{}` unless `problemSlug(body.type) === 'validation'` and `body.errors` is an object. It keeps only the keys whose value is an array of strings.

**3. `core/problems/problem-messages.spec.ts`** (new): `problemFieldErrors` returns the errors of a validation body; `{}` for a non-validation type, `null` or a string; and drops non-string-array values. `problemMessage` returns `PROBLEM_MESSAGES['not-found']` and `['concurrency-conflict']` for those types (prompt 02 added both).

**4. `items/items.service.ts`:** `@Injectable({ providedIn: 'root' }) export class ItemsService`, exporting `Item`, `ItemInput` (`{ sku; name; baseUnit }`) and `ItemQuery` (`{ search?: string; active?: boolean; page: number; pageSize: number }`).
- `list(query): Promise<Paged<Item>>` builds `HttpParams`: it omits a blank or whitespace `search` and an undefined `active`, and always sends `page` and `pageSize`.
- `get(id)`, `create(input)`, `update(item: Item, input)` (sends `{ ...input, version: item.version }`), and `deactivate(id)` (`POST` with a `null` body).
- Each returns `firstValueFrom(...)`. Encode ids with `encodeURIComponent`.

**5. `items/items.service.spec.ts`:** use `HttpTestingController`. Assert:
- `list({ search: ' rice ', active: true, page: 2, pageSize: 25 })` hits `/api/items` with `search=rice` (trimmed), `active=true`, `page=2`, `pageSize=25`.
- A blank search is omitted.
- `update` sends the item's `version`.
- `deactivate` is a `POST` to `/api/items/<id>/deactivate`.

**6. `items/item-dialog.ts` + `.html`:** a standalone component opened with `MatDialog`. The data is `{ item: Item | null }` (`MAT_DIALOG_DATA`): `null` creates, otherwise it edits.
- Typed form: `sku` (required, maxLength 64), `name` (required, 200), `baseUnit` (required, 16), all `nonNullable` and prefilled when editing.
- The title is "New item" or "Edit item". Save is disabled while the form is invalid or `saving()`. Cancel closes with `undefined`.
- Save calls `items.create(value)` or `items.update(item, value)`, then `dialogRef.close(saved)`.
- On an `HttpErrorResponse`: for each key `problemFieldErrors(err.error)` returns that names a control, call `control.setErrors({ server: messages[0] })` and show it in the `mat-error`. Otherwise (including `409`), show `problemMessage(err.error)` in a form-level `role="alert"` element. Then re-enable Save.
- Use `mat-dialog-title`, `mat-dialog-content` and `mat-dialog-actions`. Fields stack full-width, so the dialog works at 375 px. Add the hint "Lowercase, e.g. pc, kg, l, box" under the unit field.

**7. `items/item-dialog.spec.ts`:** provide `MAT_DIALOG_DATA`, a `MatDialogRef` stub `{ close: vi.fn() }` and an `ItemsService` stub with `vi.fn()` methods. Assert:
- Save is disabled when empty.
- Create calls `create` with the form value and closes with the result.
- Edit prefills, and calls `update(item, value)`.
- A 400 `{ type: '/problems/validation', errors: { sku: ['An item with this SKU already exists.'] } }` shows that text and leaves the dialog open.
- A 409 `/problems/concurrency-conflict` shows `PROBLEM_MESSAGES['concurrency-conflict']`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Install AG Grid or add routes or nav (prompt 08), or add any npm package.
- Change the existing entries in `PROBLEM_MESSAGES`, or `problemMessage`/`problemSlug`.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- The three new spec files pass, and the existing specs stay green.
