---
type: prompt
plan: "[[2026-11-13-s7-stock-screens]]"
id: 07
unit: IMS-11
status: todo
model: sonnet
effort: high
depends_on: [05]
parallel_group: G
files:
  create:
    - web/src/app/documents/posting-form.ts
    - web/src/app/documents/posting-form.spec.ts
  modify: []
  delete: []
---
# 07 · Posting form logic (T13)

## Task
Write the pure, framework-light logic behind the post-stock form, with an exhaustive Vitest spec. It covers request bodies per type, the Idempotency-Key holder, mapping ProblemDetails to header and line errors, outflow-over-on-hand warnings, and the amount validator. This is T13's "line editor" and "idempotency key reuse" (spec §11). The component (prompt 08) only wires it to the template.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-11`. Angular 22, TypeScript ~6.0, Vitest. No new package.
- From prompt 05 (`web/src/app/documents/documents.service.ts`): `PostingType`, `ReceiptBody`, `IssueBody`, `TransferBody`, `AdjustmentBody`, `PostingBody`. From `web/src/app/shared/format.ts`: `AdjustmentReason`, `formatQuantity`. From `web/src/app/core/problems/problem-messages.ts`: `problemSlug(type)`, `problemMessage(body)`, `problemFieldErrors(body)` (validation `errors`, keyed as the API sends them).
- Server rules this must agree with (`src/Modules/Inventory/Inventory/Features/Posting/PostingValidation.cs`): at most 100 lines (`:45`); an item may appear on only one line, else `400` `lines[i].itemId` (`:246-248`); the server trims `reference`/`notes`. Error keys: header `locationId`, `fromLocationId`, `toLocationId`, `reasonCode`, `reference`, `notes`, `lines`; per line `lines[i].itemId`, `lines[i].quantity`, `lines[i].quantityDelta` (adjustments, IMS-10 D5: map it to the quantity field), `lines[i].unitCost` (it can arrive after submit on an adjustment into an empty balance, A7). Problem bodies (spec §9): `insufficient-stock` has `itemId`, `locationId`, `available`, `requested`; `idempotency-key-reused` has `originalDocumentId`; `inactive-item` has `itemId`; `inactive-location` has `locationId`.
- **Exports of `posting-form.ts`:**
  ```ts
  export interface PostingHeader {
    locationId: string | null; fromLocationId: string | null; toLocationId: string | null;
    reasonCode: AdjustmentReason | null; reference: string; notes: string;
  }
  export type LineDirection = 'add' | 'remove';
  export interface PostingLine { itemId: string | null; quantity: number | null; unitCost: number | null; direction: LineDirection; }
  export const POSTING_TITLES: Record<PostingType, string>; // receipt 'Receive stock', issue 'Use or sell stock', transfer 'Move stock', adjustment 'Correct stock' (spec §10 Labels)
  export const MAX_LINES = 100;
  export function usesUnitCost(type: PostingType, line: Pick<PostingLine, 'direction'>): boolean; // receipt: true; adjustment: direction === 'add'; else false
  export function unitCostRequired(type: PostingType): boolean; // receipt only (an adjustment's cost is optional; the server may still ask, A7)
  export function isOutflow(type: PostingType, line: Pick<PostingLine, 'direction'>): boolean; // issue, transfer: true; adjustment: direction === 'remove'; receipt: false
  export function sourceLocationId(type: PostingType, header: PostingHeader): string | null; // transfer: fromLocationId; else locationId
  export function buildPostingBody(type: PostingType, header: PostingHeader, lines: readonly PostingLine[]): PostingBody;
  export function linesOverOnHand(type: PostingType, lines: readonly PostingLine[], onHand: readonly (number | null)[]): number[];
  export function duplicateItemLines(lines: readonly PostingLine[]): number[]; // indices of the 2nd+ occurrence of an itemId
  export function amountValidator(options: { allowZero: boolean }): ValidatorFn; // from @angular/forms
  export class IdempotencyKey {
    constructor(generate?: () => string); // default () => crypto.randomUUID()
    get current(): string;
    renew(): void;
  }
  export interface PostingErrors {
    header: Partial<Record<keyof PostingHeader | 'lines', string>>;
    lines: Record<number, Partial<Record<'itemId' | 'quantity' | 'unitCost', string>>>;
    message: string | null;            // shown above the form; null when every error landed on a field
    originalDocumentId: string | null; // set on idempotency-key-reused
    renewKey: boolean;                 // true only on idempotency-key-reused
  }
  export function postingErrors(type: PostingType, body: unknown, lines: readonly PostingLine[], units?: readonly (string | null)[]): PostingErrors;
  ```
- **Rules:**
  - `buildPostingBody`. Trim `reference`/`notes` and send `null` when blank. Send lines in array order, which is the order the server indexes `lines[i]`.
    - receipt: `{ locationId, reference, notes, lines: [{ itemId, quantity, unitCost }] }`.
    - issue: `{ locationId, reference, notes, lines: [{ itemId, quantity }] }`.
    - transfer: `{ fromLocationId, toLocationId, reference, notes, lines: [{ itemId, quantity }] }`.
    - adjustment: `{ locationId, reasonCode, reference, notes, lines: [{ itemId, quantityDelta: direction === 'add' ? quantity : -quantity, unitCost: direction === 'add' ? unitCost : null }] }` (plan D9; a unit cost on a negative line is a server `400`).
    - Emit exactly these keys in this order (no extra keys, no `undefined`). The server canonicalizes the hash (S5c), so a retry only has to resend the same values. The spec asserts `toEqual` on each type's exact body.
  - `linesOverOnHand`: indices `i` where `isOutflow(type, lines[i])`, `quantity != null`, `onHand[i] != null` and `quantity > onHand[i]`.
  - `amountValidator`: `null` for an empty value (leave "required" to `Validators.required`). Error `{ amount: 'Enter a number.' }` for a non-finite value. `{ amount: 'Must be more than 0.' }` (or `'Cannot be negative.'` when `allowZero`) for a value below the minimum. `{ amount: 'Use at most 4 decimal places.' }` when `Number(value.toFixed(4)) !== value`. `{ amount: 'Must be at most 999,999,999.9999.' }` when `Math.abs(value) > MAX_AMOUNT`, where the module constant `MAX_AMOUNT = 999_999_999.9999` mirrors the server's `PostingValidation.MaxAmount` (`src/Modules/Inventory/Inventory/Features/Posting/PostingValidation.cs:40`; say so in a comment, audit FIX 1). That range (13 significant digits) keeps every 4-dp value exact in a double, so `JSON.stringify` sends what the user typed (plan D7).
  - `IdempotencyKey`: generates once in the constructor; `current` is stable across any number of reads (retries, double-clicks); `renew()` generates a new one.
  - `postingErrors`:
    - `validation`: map each key from `problemFieldErrors(body)`. A header key goes to `header`. `lines[i].quantityDelta` and `lines[i].quantity` go to `lines[i].quantity`; `lines[i].unitCost` and `lines[i].itemId` go to their own fields; `lines` and `lines[i]` go to `header.lines`. Use the first message of each. Any key that maps nowhere makes `message` = `problemMessage(body)`; otherwise `message` is null.
    - `insufficient-stock`: every line whose `itemId` equals the body's `itemId` gets `quantity: 'Only {available} available; you asked for {requested}.'` (with `formatQuantity`, plus `' ' + units[i]` when given). `message` = `problemMessage(body)`.
    - `inactive-item`: lines with that `itemId` get `itemId: 'This item is no longer active.'`. `inactive-location`: `message` = `problemMessage(body)`.
    - `idempotency-key-reused`: `originalDocumentId` from the body, `renewKey: true`, `message` = `problemMessage(body)`.
    - Anything else (including a non-object body, a network error or a `500`): `message` = `problemMessage(body)`, and `renewKey: false`, so the same key is reused on retry (spec §10).
- **`posting-form.spec.ts`** (T13). Name the `describe` blocks `line editor`, `idempotency key` and `amounts`. Cover:
  - The exact body of each of the four types, including a blank `reference` becoming `null`, an adjustment add line (positive delta, cost kept), and a remove line (negative delta, `unitCost: null`).
  - `usesUnitCost`, `isOutflow`, `sourceLocationId` for every type/direction.
  - `linesOverOnHand` with an outflow over, equal (not flagged), unknown on-hand (not flagged), and receipt lines (never flagged).
  - `duplicateItemLines`.
  - `amountValidator` for every branch, including `0.1` valid, `0.12345` invalid, `999_999_999.9999` valid, and `1_000_000_000` invalid.
  - `IdempotencyKey` with an injected counter generator: `current` is the same on repeated reads, and `renew()` changes it once.
  - `postingErrors` for each slug above, including `lines[1].quantityDelta` landing on line 1's `quantity`, `insufficient-stock` marking both lines of a duplicated item, an unknown key setting `message`, and `idempotency-key-reused` giving `renewKey` and `originalDocumentId`. Also a `{ status: 0 }`-like non-problem body keeps `renewKey: false`.

## Do not
- Import Angular components, Material or `HttpClient` here (only `@angular/forms` types for the validator).
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- `posting-form.spec.ts` passes, with the three named `describe` blocks, and every existing spec passes unmodified.
