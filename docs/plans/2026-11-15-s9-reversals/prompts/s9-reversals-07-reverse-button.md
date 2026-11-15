---
type: prompt
plan: "[[2026-11-15-s9-reversals]]"
id: 07
unit: IMS-13
status: todo
model: sonnet
effort: medium
depends_on: [06]
parallel_group: E
files:
  create: []
  modify:
    - web/src/app/documents/document-detail.ts
    - web/src/app/documents/document-detail.html
    - web/src/app/documents/document-detail.spec.ts
  delete: []
---
# 07 · Web: Reverse button

## Task
Add the **Reverse** button to the document detail screen (spec §10 `:604`, plan D11): Admin only, hidden on reversals and on documents already reversed, with a confirm step, an `Idempotency-Key` reused on retry, the button disabled in flight, and a reload after success. Cover it with Vitest.

## Context
- Repo: `/Users/alessandrocruz/Code/inventory-management-system`, branch `IMS-13`, Angular app in `web/` (standalone components, signals, zoneless, `@if`/`@for`, Angular Material). Prompt 06 added `DocumentsService.reverse(id: string, idempotencyKey: string): Promise<PostedDocument>` and the three reversal messages in `PROBLEM_MESSAGES`.
- Current screen: `web/src/app/documents/document-detail.ts` (loads by `paramMap` id, `requestedId` guard, signals `document`, `error`, `loading`) and `document-detail.html` (header `dl`, "Reversed by" / "Reverses" links `:28-33`, lines table).
- Reuse: `IdempotencyKey` from `./posting-form` (`posting-form.ts:172-190`: `current`, `renew()`; constructor takes an optional generator); `ConfirmDialog` from `../shared/confirm-dialog` opened as in `items/items.ts:120-145` (`MatDialog.open<ConfirmDialog, ConfirmDialogData, boolean>(…).afterClosed()`); `AuthService.isAdmin` (a computed signal, `core/auth/auth.service.ts:20`); `problemMessage`, `problemSlug`, `PROBLEM_MESSAGES` from `../core/problems/problem-messages`; `MatButtonModule`.
- **Behaviour (D11):**
  - `canReverse = computed(() => { const d = this.document(); return this.auth.isAdmin() && d !== null && d.type !== 'Reversal' && d.reversedByDocumentId === null; })`.
  - Template: under the reversal links, `@if (canReverse()) { <button mat-flat-button type="button" (click)="reverse()" [disabled]="reversing()">Reverse</button> }`. Below it, `@if (reverseError(); as e) { <p role="alert">{{ e.message }} @if (e.linkId) { <a [routerLink]="['/documents', e.linkId]">{{ e.linkLabel }}</a> } </p> }`.
  - `private reverseKey = new IdempotencyKey();` renewed in `load(id)` whenever the id differs from the last loaded one, after a success, and on `idempotency-key-reused`. A failed attempt (network error, `409 concurrency-conflict`, `422`, …) keeps the key, so a retry or a double-click never posts twice (spec §10).
  - `reverse()`: if `reversing()` return. Open `ConfirmDialog` with `{ title: 'Reverse document', message: \`Reverse this ${typeLabel(doc.type).toLowerCase()} document? A reversal document undoes its stock movements. It can't be edited later.\`, confirm: 'Reverse' }`. On `true`: `reversing.set(true)`, `reverseError.set(null)`, `await documentsService.reverse(doc.id, reverseKey.current)`; on success `reverseKey.renew()` and `await this.load(doc.id)` (the reload shows "Reversed by this reversal" and hides the button). On `HttpErrorResponse`, with `body = err.error` and `slug = problemSlug(body?.type)`:
    - `reversal-not-latest` → message `PROBLEM_MESSAGES['reversal-not-latest']`, link `body.blockingDocumentId`, label `'Open the later document'`.
    - `already-reversed` → message `PROBLEM_MESSAGES['already-reversed']`, link `body.reversalDocumentId`, label `'Open the reversal'`; then `await this.load(doc.id)`. `load()` resets `error` and `document`; it must not reset `reverseError` when reloading the same id, so the message and link survive the reload.
    - `idempotency-key-reused` → `reverseKey.renew()`, message `problemMessage(body)`.
    - anything else → `problemMessage(body)`.
    Always `reversing.set(false)` in `finally`.
  - `reverseError` is `signal<{ message: string; linkId: string | null; linkLabel: string } | null>(null)`, cleared only when a different document id loads (and at the start of each `reverse()`).
- **`document-detail.spec.ts`** — keep every existing test passing. Extend the `AuthService` stub to `{ user: signal({ id: 'u-1' }), isAdmin: signal(false) }` (keep a handle to set it per test), add `reverse = vi.fn()` to the `DocumentsService` stub, and provide `MatDialog` as `{ open: vi.fn(() => ({ afterClosed: () => of(confirmed) })) }` with a `confirmed` flag per test. New tests:
  1. Admin + Receipt + not reversed → a button with text `Reverse` exists.
  2. Not shown for Staff (`isAdmin` false), for `type: 'Reversal'`, or when `reversedByDocumentId` is set.
  3. Confirm → `reverse` called once with `('d1', <key>)`, then `get` called a second time; button absent after the reload when the reloaded doc has `reversedByDocumentId: 'r1'`.
  4. Cancel (`confirmed = false`) → `reverse` not called.
  5. `reversal-not-latest` (`HttpErrorResponse` status 409, `error: { type: '/problems/reversal-not-latest', blockingDocumentId: 'b1' }`) → the alert shows its message and a link to `/documents/b1`.
  6. A failed attempt (status 0) then a second click → both `reverse` calls carry the same key.
  7. `already-reversed` with `reversalDocumentId: 'r9'` → link to `/documents/r9` and `get` called again.
  Use `await fixture.whenStable(); fixture.detectChanges();` as the existing `render()` does.
- Spec §10 conventions: plain verbs, phone width (the button sits in the normal flow; no new styles needed). Keep component styles inline and short (Angular budget).

## Do not
- Touch files outside the `files` list (no route, no service, no messages changes).
- Show the button to Staff or rely on hiding it for security (the API enforces Admin).
- Add packages.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
git diff --stat        # only the three declared files
```
- As Admin on a receipt, Reverse → confirm → the page reloads showing "Reversed by this reversal" (manual smoke in the plan's end-to-end block).
