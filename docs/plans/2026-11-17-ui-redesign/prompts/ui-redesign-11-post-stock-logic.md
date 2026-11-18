---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 11
unit: IMS-26
status: done
model: sonnet
effort: medium
depends_on: [IMS-22]
parallel_group: A
files:
  create: []
  modify:
    - web/src/app/documents/posting-form.ts
    - web/src/app/documents/posting-form.spec.ts
    - web/src/app/stock/stock.service.ts
    - web/src/app/stock/stock.service.spec.ts
  delete: []
---
# 11 · Post-stock total, subtitles and balance lookup

## Task
Add the pure pieces the redesigned post form needs, with tests:
- Per-type subtitles.
- The running total and its label.
- A stock lookup that returns the average cost alongside on-hand.

Don't touch any component; prompt 12 wires these in.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`.
- **Spec:** `docs/design_handoff_inventory_redesign/README.md` §7 "Post stock" (titles and subtitles, the sticky action bar). The prototype's total rule is `Inventory App.dc.html:800-823`: per line, `showCost ? q * c : q * avgCost`, summed. `showCost` is a receipt or an adjustment Add line. The label is "Total cost" when the type has a cost field (receipt, adjustment), else "Value at average cost".
- **Plan D14:** the total is display-only (the server computes the real costs). It reuses the existing helpers in `web/src/app/documents/posting-form.ts`: `usesUnitCost(type, line)` (`:45`) is the `showCost` rule; `PostingLine` (`:24`) is `{ itemId, quantity, unitCost, direction }`; `POSTING_TITLES` (`:31`) is the pattern to copy.
- `web/src/app/stock/stock.service.ts:51-54`: `onHand(itemId, locationId)` calls `list({ itemId, locationId, page: 1, pageSize: 1 })` and returns `items[0]?.onHand ?? 0`. Its only caller is `post-stock.ts:419`. `StockRow` has `averageCost: number | null`.
- Tests: copy the style of `posting-form.spec.ts` and `stock.service.spec.ts` (it uses `HttpTestingController`; read the file to match).

### `posting-form.ts`: append
```ts
export const POSTING_SUBTITLES: Record<PostingType, string> = {
  receipt: 'Record stock arriving from a supplier.',
  issue: 'Record stock that was sold or used up.',
  transfer: 'Move stock between two of your locations.',
  adjustment: 'Fix a count, or write off damaged or lost stock.',
};

/** "Total cost" for types with a cost field (receipt, adjustment); else "Value at average cost". */
export function postingTotalLabel(type: PostingType): 'Total cost' | 'Value at average cost'

/**
 * The sticky-bar total (display only; the server costs the posting). Per line:
 * usesUnitCost → quantity × (unitCost ?? 0); otherwise quantity × (averageCosts[i] ?? 0).
 * Lines with a null or non-finite quantity count as 0. Rounded to 4 dp, AwayFromZero
 * (Math.sign(x) * Math.round(Math.abs(x) * 10000) / 10000).
 */
export function postingTotal(type: PostingType, lines: readonly PostingLine[], averageCosts: readonly (number | null)[]): number
```
Tests:
- A receipt with 2 lines: 10 × 85.5 + 2 × 142 = 1139.
- An issue using the average costs `[85.5, null]` with quantities 3 and 2: 256.5.
- An adjustment with an Add line (2 × 10) and a Remove line (1, average cost 7): 27. This follows the prototype, which counts Remove lines as positive (plan Assumptions).
- A null quantity is ignored.
- A rounding case such as 0.1 × 3 is 0.3.
- `postingTotalLabel` for all four types.

### `stock.service.ts`
```ts
export interface StockBalance { onHand: number; averageCost: number | null; }

async balance(itemId: string, locationId: string): Promise<StockBalance> {
  const page = await this.list({ itemId, locationId, page: 1, pageSize: 1 });
  const row = page.items[0];
  return { onHand: row?.onHand ?? 0, averageCost: row?.averageCost ?? null };
}

async onHand(itemId: string, locationId: string): Promise<number> {
  return (await this.balance(itemId, locationId)).onHand;
}
```
Keep `onHand`'s existing spec passing. Add a `balance` test: a row returns both fields; an empty page returns `{ onHand: 0, averageCost: null }`; one request with `itemId`, `locationId`, `page=1`, `pageSize=1`.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Change any existing export's behaviour in `posting-form.ts` (`buildPostingBody`, `postingErrors`, `linesOverOnHand`, validators, `IdempotencyKey`).
- Add dependencies, or create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)/web"
npm run lint && npm run build && npm test -- --watch=false
```
- The new `posting-form.spec.ts` and `stock.service.spec.ts` cases pass, and no existing case changed.
