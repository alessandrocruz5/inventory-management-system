---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 07
unit: IMS-24
status: done
model: sonnet
effort: medium
depends_on: [06]
parallel_group: B
files:
  create:
    - web/src/app/items/item-detail.scss
  modify:
    - web/src/app/items/item-detail.ts
    - web/src/app/items/item-detail.html
    - web/src/app/items/item-detail.spec.ts
  delete: []
---
# 07 · Item detail screen

## Task
Restyle `/items/:id` to the handoff:
- A "← Stock" back link.
- The item name as the title, with a status pill.
- A meta line (SKU, Unit, On hand).
- "Stock by location" and "History" sections, each a card with a sideways-scrolling table; History keeps its paginator.

The data loading, the stale-response guards (`requestedId`) and paging are unchanged.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`.
- IMS-22 globals in `web/src/styles.scss` (don't edit it): `.page`, `.back-link`, `.page-title`, `.section-title`, `.card`, `.card__footer`, `.data-table`, `.table-scroll`, `.num`, `.mono`, `.muted`, `.secondary`, `.strong`, `.empty-state`, `.banner`, `.banner--bad`, `.skeleton*`. Also `StatusPill` (`shared/status-pill.ts`). The `mat-icon` default font is Material Symbols. The shell pads the main area.
- **Spec:** `docs/design_handoff_inventory_redesign/README.md` §3 "Item detail" and "Tables". Screen: `screenshots/desktop-03-item-detail.png`. Prototype: `Inventory App.dc.html?screen=item`.
- **User decision (plan D3):** the History "Document" link shows the type label only (`typeLabel(m.documentType)`), with no `· REF`, because the movements API has no reference.
- Current code: `item-detail.ts:21-83` (inline `styles`, which move to `item-detail.scss`, dropping the mobile `data-label` card rules), `item-detail.ts:115-131` (signed formatting, counterpart, posted-by), `item-detail.html`.

### Layout
- `host: { class: 'page' }`.
- **Always first** in the template, even in the error state: `<a class="back-link" routerLink="/stock"><mat-icon>arrow_back</mat-icon>Stock</a>`. `item-detail.spec.ts:155` expects the first `a` to have `href` `/stock` on a 404.
- **Error:** `<p class="banner banner--bad" role="alert"><mat-icon>error</mat-icon>{{ message }}</p>`. The old "Back to stock" link goes, because the back link covers it.
- **Title row:** flex, wrap, gap `8px 12px`, `align-items: center`. `<h1 class="page-title">{{ it.name }}</h1>`, then `<app-status-pill>`: Active `good` or Inactive `neutral`. Today's spec expects the text "Inactive" for an inactive item, and the pill provides it.
- **Meta line:** flex, wrap, `gap: 8px 24px`, 14 px, labels secondary and values ink:
  - "SKU" with `it.sku` in `.mono`.
  - "Unit" with `it.baseUnit`.
  - "On hand" with `formatQuantity(totalOnHand()) + ' ' + baseUnit`, where `totalOnHand` is a `computed` summing `item().balances[].onHand`. This is display-only: round with `Math.round(x * 10000) / 10000` before formatting, so float sums don't print extra digits.
- **"Stock by location"** (`<h2 class="section-title">`), then a `.card` with `.table-scroll` around `<table class="data-table">` (`min-width: 520px`):
  - Columns: Location (`<span class="mono">CODE</span><span class="muted"> · Name</span>`), Qty (`.num`, with the unit), Avg cost (`.num`, "—"), Value (`.num`).
  - When there are no balances, show `<p class="empty-state">No stock at any location yet.</p>` inside the card. Keep that text.
- **"History"**, then a `.card` with a `.table-scroll` table (`min-width: 960px`):
  - Columns: Posted (`.secondary`, tabular, nowrap); Document (a link to `['/documents', m.documentId]` with the type label); Location (`.mono` with a `title` of the name); From / to (as today); Qty (`.num`, signed, e.g. "+24"); Value (`.num`, signed); Balance (`.num`); Balance value (`.num`); Posted by.
  - Empty: `<p class="empty-state">No movements yet.</p>`. Keep that text.
  - The existing `mat-paginator` goes in `.card__footer`.
- **Loading:** while `loading()` and `item()` is null and there's no error, show a `.card` with 4 `.skeleton-row`s.
- `item-detail.scss`: only what the globals don't cover (title row, meta line), under 1.5 kB. Use `styleUrl`.

### `item-detail.spec.ts`
Keep all existing cases. The hrefs `/documents/d6` and `/documents/d5` still exist, as do the empty-state texts and "Inactive". Adapt only selectors or texts that the new structure changed. Add:
- The title `h1` is the item's name.
- The meta line shows "On hand" with the sum of the balances.
- The back link is `/stock` in the loaded state too.

## Do not
- Touch files outside the `files` list. If you must, stop and report why. Not `items.service.ts` or `styles.scss`.
- Change the loading, paging or stale-guard logic, or add an API field.
- Add dependencies, or create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)/web"
npm run lint && npm run build && npm test -- --watch=false
```
- `/items/<id>` at 1280 px matches `desktop-03-item-detail.png`. At 375 px both tables scroll inside their cards, `document.documentElement.scrollWidth <= 375`, and the back link and the History paginator buttons measure ≥ 44 px tall from IMS-22's mobile block (plan D19; don't override it in `item-detail.scss`).
