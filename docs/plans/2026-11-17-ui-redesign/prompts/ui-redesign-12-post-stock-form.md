---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 12
unit: IMS-26
status: done
model: sonnet
effort: high
depends_on: [11]
parallel_group: B
files:
  create: []
  modify:
    - web/src/app/documents/post-stock.ts
    - web/src/app/documents/post-stock.html
    - web/src/app/documents/post-stock.scss
    - web/src/app/documents/post-stock.spec.ts
  delete: []
---
# 12 · Post-stock form layout

## Task
Restyle `/documents/new/:type` to the handoff:
- Title and subtitle.
- The error banner above a "Details" card (header fields with labels above).
- A "Lines" card of wrapping line rows. Each row has the Item autocomplete, the Add/Remove segment, Quantity · unit, Unit cost · ₱ and a remove icon button, with a full-width on-hand hint or negative warning below it.
- A dashed "+ Add line".
- A sticky action bar with the running total, Cancel and Post.

All form logic stays as is: validation, idempotency key, the over-on-hand confirm, the server-error mapping, navigation after a post.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`.
- IMS-22 globals in `web/src/styles.scss` (don't edit it): `.page`, `.page--narrow` (max 880 px), `.page-header`, `.page-title`, `.page-subtitle`, `.card`, `.card__body`, `.card-title`, `.field`, `.field-label`, `.field-hint`, `.num`, `.muted`, `.tone-bad`, `.banner`, `.banner--bad`, `.btn-form`, `.btn-icon`, `.btn-icon--42`, `.btn-dashed`, `.segmented` (for `mat-button-toggle-group`). Form fields default to `outline` with a dynamic subscript. The `mat-icon` default font is Material Symbols. The shell pads the main area.
- Prompt 11 added `POSTING_SUBTITLES`, `postingTotal(type, lines, averageCosts)` and `postingTotalLabel(type)` to `posting-form.ts`, plus `StockService.balance(itemId, locationId)` returning `{ onHand, averageCost }`. Existing helpers: `isOutflow(type, line)` (`posting-form.ts:53`), `usesUnitCost`, `sourceLocationId`.
- **Spec:** `docs/design_handoff_inventory_redesign/README.md` §7 "Post stock", "Inputs and selects", "Buttons". Screens: `screenshots/desktop-07-receive-stock.png`, `desktop-08-correct-stock.png` and `mobile-use-or-sell-stock.png`. Prototype: `Inventory App.dc.html?screen=post&type=adjustment` (and the other types).
- **User decision (plan D2):** the Item field stays the `mat-autocomplete` input (spec §10, "item autocomplete by SKU or name"), restyled. It isn't a native select.
- Current code: `post-stock.html` (all of it) and `post-stock.ts`. Today the on-hand text comes from `onHandText(i)` (`:166-173`) and the lookup from `refreshOnHand` (`:409-431`, which calls `stock.onHand`). `onHand` is a `signal<(number|null)[]>` kept in step with the lines in `addLine`, `removeLine`, `reset` and `setOnHand`.

### Hard contracts (e2e `tests/e2e/support/app.ts:76-96`, `specs/post-stock-mobile.spec.ts`)
- **Accessible names**, using the label-above pattern of plan D6 (a `<label class="field-label" for|id>` and no `mat-label`):
  - `getByRole('combobox', { name: 'Location' })`: the Location `mat-select` gets `aria-labelledby="post-location-label"`. Its options stay `CODE · Name`.
  - `getByLabel('Reference')` → `<input matInput id="post-reference">` with `<label for="post-reference">Reference</label>`.
  - `getByRole('combobox', { name: 'Item' })` → the autocomplete input gets id `post-item-{{ i }}` with `<label for="post-item-{{ i }}">Item</label>`. Its options stay `SKU · Name`.
  - `getByLabel('Quantity')` → `<label for="post-qty-{{ i }}">Quantity <span class="field-hint">· {{ unit }}</span></label>`. Show the hint only when an item is chosen. Playwright matches names by substring, so the hint is fine.
  - `getByLabel('Unit cost')` → `<label for="post-cost-{{ i }}">Unit cost <span class="field-hint">· ₱</span></label>`.
  - The same pattern for From, To, Reason (`aria-labelledby`) and Notes (`<span class="field-hint">· optional</span>`).
  - The e2e posts one line, so the `i = 0` ids must work. Every id must be unique per line.
- **`getByRole('button', { name: 'Post' })`** must match exactly one button: the submit button with the text "Post". Cancel is a link (`<a mat-stroked-button routerLink="/stock">Cancel</a>`, plan Assumptions). The remove button is named "Remove line", and "+ Add line" is named "Add line". Neither contains "post".
- At 375 px, `document.documentElement.scrollWidth <= 375`. Every row wraps, and the sticky bar wraps its buttons under the total if needed.

### Layout
- `host: { class: 'page page--narrow' }`.
- **Header:** `<h1 class="page-title">{{ title() }}</h1>` and `<p class="page-subtitle">{{ subtitle() }}</p>`, with `subtitle = computed(() => POSTING_SUBTITLES[this.type()])`.
- **Inside the `<form>`, in this order:**
  1. **Error banner:** `@if (message())` → `<p class="banner banner--bad" role="alert"><mat-icon>error</mat-icon><span>{{ text }} @if (originalDocumentId(); as docId) { <a [routerLink]="['/documents', docId]">Open the original posting</a> }</span></p>`. If `originalDocumentId` is set without a message, still render that link (today it is rendered on its own). Keep the link as the first `<a>` in the form, because `post-stock.spec.ts:145` reads it.
  2. **"Details" card:** `.card > .card__body` with `<h2 class="card-title">Details</h2>`, then a grid `repeat(auto-fit, minmax(min(100%, 220px), 1fr))` with gap 16 and `margin-top: 16px`.
     - Fields: Location (or From and To for a transfer), Reason (adjustment), Reference.
     - Then Notes, a 2-row `textarea` spanning `1 / -1`, with the placeholder "Anything the next person should know".
     - Keep every `formControlName`, `maxlength` and `<mat-error>{{ errorText(…) }}</mat-error>`.
  3. **"Lines" card:**
     - The card header: `padding: 16px clamp(16px,2.4vw,24px)`, flex `space-between`, holding `<h2 class="card-title">Lines</h2>` and `{{ lineCount() }} line(s)` (13 px muted; "1 line" singular).
     - For each line, a `.line` (`[formGroup]="line"`): flex-wrap, `gap: 12px 16px`, `align-items: flex-end`, `padding: 16px clamp(16px,2.4vw,24px)`, border-top 1 px divider.
       - **Item** `.field` (`flex: 3 1 240px`): the autocomplete, unchanged apart from the label pattern.
       - **Adjustment only:** the Direction `.field` with a visually hidden label "Direction", and `<mat-button-toggle-group class="segmented" formControlName="direction" aria-label="Direction">` with Add and Remove.
       - **Quantity** `.field` (`flex: 1 1 120px`): `<input matInput type="number" class="num" …>`.
       - **Unit cost** `.field` (`flex: 1 1 120px`), `@if (showUnitCost(line))`.
       - **Remove:** `<button mat-stroked-button class="btn-icon btn-icon--42" type="button" aria-label="Remove line" [disabled]="lineCount() <= 1" (click)="removeLine(i)"><mat-icon>delete</mat-icon></button>`.
       - **The hint** (`flex-basis: 100%`, 12/16, flex, gap 6, 16 px icon), the first that applies:
         1. Duplicate item (`duplicates().includes(i)`): `tone-bad`, icon `error`, "This item is already on another line." (keep the text).
         2. Outflow over on-hand (`isOutflow(type, line)`, quantity > on-hand): `tone-bad`, icon `error`, "Only {qty} {unit} on hand at {LOC}. This will take the balance negative."
         3. On-hand known: `muted`, icon `inventory_2`, "On hand at {LOC}: {qty} {unit}".
       - Here `{LOC}` is the source location's `code`, looked up in `locations()` by `sourceLocationId(type, header)`. `{qty}` is `formatQuantity`. Compute the hint in a `protected lineHint(i): { tone: 'muted' | 'bad'; icon: string; text: string } | null` that replaces `onHandText`.
     - Then, in a padded footer, `<button class="btn-dashed" mat-stroked-button type="button" [disabled]="lineCount() >= maxLines" (click)="addLine()"><mat-icon>add</mat-icon>Add line</button>`.
  4. **Sticky bar** (`.post-bar`): `position: sticky; bottom: 0; z-index: 5`, white, 1 px border, radius 12, shadow `0 -4px 16px rgba(28,27,25,.04)`, `padding: 12px clamp(16px,2.4vw,24px)`, flex-wrap, `justify-content: space-between`, `align-items: center`, gap 12.
     - Left: `{{ totalLabel() }}` (12 px muted) above `{{ formatMoney(total()) }}` (18/26 600, tabular).
     - Right (flex, gap 8): Cancel (`a mat-stroked-button class="btn-form" routerLink="/stock"`), then `<button mat-flat-button class="btn-form" type="submit" [disabled]="!canSubmit()">Post</button>`.
- **`post-stock.ts` changes:**
  - Add `averageCost = signal<(number | null)[]>([null])` and keep it in step with `onHand` everywhere `onHand` changes length or is reset.
  - `refreshOnHand` calls `this.stock.balance(...)`. With the same stale guard as today, it sets both arrays, through a `setBalance(group, value: StockBalance | null)` that replaces `setOnHand`.
  - `total = computed(() => postingTotal(this.type(), this.lineValues(), this.averageCost()))`, `totalLabel = computed(() => postingTotalLabel(this.type()))`, `subtitle`, and `lineHint(i)`.
  - Import `MatIconModule`, plus `formatMoney` from `shared/format`.
  - `submit()`, `showErrors()`, `reset()`, `makeLine()`, `search()` and the validators keep their logic; only update the `onHand`/`averageCost` bookkeeping lines inside them.
- **`post-stock.scss`:** replace it with the details grid, `.line`, the hint, the lines-card header and footer, and `.post-bar`. Stay under 3.5 kB (the component budget warns at 4 kB).

### `post-stock.spec.ts`
Keep every case's intent: types, validation, the idempotency key, the over-on-hand confirm, the error mapping. Update only selectors and texts:
- The on-hand text is now "On hand at CODE: 5 pc".
- The remove button is found by `aria-label="Remove line"`.
- `:145` reads the original-posting link.
- `:188` finds a button. Match it by `type="submit"` or by `aria-label`, because icon ligature text is in `textContent`.
- Mock `StockService.balance` where the spec mocked `onHand`.

Add:
- The subtitle for `issue`.
- The total label: "Total cost" for receipt, "Value at average cost" for issue.
- A receipt with qty 2 and cost 10 shows ₱20.00 in `.post-bar`.
- An issue line over on-hand shows the negative warning text.
- The Location `mat-select` has `aria-labelledby="post-location-label"`, and `label[for="post-reference"]` exists.

## Do not
- Touch files outside the `files` list. If you must, stop and report why. Not `posting-form.ts`, `stock.service.ts`, `styles.scss` or the e2e suite.
- Change validation messages, the confirm dialog text, the idempotency handling or the post-success navigation.
- Replace the autocomplete with a select.
- Add dependencies, or create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- `/documents/new/receipt` at 1280 px matches `desktop-07-receive-stock.png`, `/documents/new/adjustment` matches `desktop-08-correct-stock.png`, and `/documents/new/issue` at 390 px matches `mobile-use-or-sell-stock.png`. At 375 px, `document.documentElement.scrollWidth <= 375`.
- CI `e2e` passes: `postDocument` for receipt and issue, and `post-stock-mobile.spec.ts`.
