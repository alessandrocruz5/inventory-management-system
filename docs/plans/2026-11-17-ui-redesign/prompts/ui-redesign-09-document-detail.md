---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 09
unit: IMS-25
status: done
model: sonnet
effort: medium
depends_on: [IMS-22]
parallel_group: A
files:
  create:
    - web/src/app/documents/document-detail.scss
  modify:
    - web/src/app/documents/document-detail.ts
    - web/src/app/documents/document-detail.html
    - web/src/app/documents/document-detail.spec.ts
    - tests/e2e/specs/t14-stock-flow.spec.ts
  delete: []
---
# 09 · Document detail screen

## Task
Restyle `/documents/:id` to the handoff:
- A "← Documents" back link.
- A title row (type label, mono reference, status pill, and a Reverse button on the right).
- A reversed or reversal notice banner.
- A divided meta grid.
- A "Lines" card with a footer total.

The reverse flow (confirm, idempotency key, error mapping, reload) is unchanged. Update the one e2e assertion that reads the old notice text.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`.
- IMS-22 globals in `web/src/styles.scss` (don't edit it): `.page`, `.back-link`, `.page-title`, `.section-title`, `.card`, `.data-table`, `.table-scroll`, `.num`, `.mono`, `.muted`, `.secondary`, `.strong`, `.banner`, `.banner--neutral|warn|bad`, `.skeleton*`. Also `StatusPill` (`shared/status-pill.ts`) and `documentStatus(doc)` (`shared/format.ts`). The `mat-icon` default font is Material Symbols. The shell pads the main area.
- **Spec:** `docs/design_handoff_inventory_redesign/README.md` §6 "Document detail". Screen: `screenshots/desktop-06-document-detail-reversed.png`. Prototype: `Inventory App.dc.html?screen=document&doc=d1` (and the other `doc` values).
- Current code: `document-detail.ts:25-83` (inline `styles`, which move to `document-detail.scss` without the mobile `data-label` rules), `:107-112` (`canReverse()`, kept as is), `:131-185` (`reverse()`, kept as is), `document-detail.html`.

### Hard contracts
- **e2e `t14-stock-flow.spec.ts`:**
  - `admin.getByRole('button', { name: 'Reverse' })` must match exactly one button before the dialog opens. A `mat-icon` inside it is `aria-hidden`, so the name stays "Reverse".
  - Staff must have no Reverse button.
  - Line 51 `getByText('Reversed by')` **changes in this prompt** to `getByText('This posting was reversed.')` (plan D15); edit only that line.
- **`document-detail.spec.ts`** reads `[role="alert"]` for the reverse error at `:183-209`. The notice banners must therefore **not** have `role="alert"`; use `role="status"`. Only the error and the reverse-error banners keep `role="alert"`. In the `already-reversed` case the page reloads into the reversed state, so a notice and an error show together, and `querySelector('[role="alert"]')` must still find the error.
- The first `<a>` in every state is the back link `/documents` (`:112` expects it on 404).

### Layout
- `host: { class: 'page' }`.
- **Always first:** `<a class="back-link" routerLink="/documents"><mat-icon>arrow_back</mat-icon>Documents</a>`.
- **Error:** `<p class="banner banner--bad" role="alert"><mat-icon>error</mat-icon>{{ message }}</p>`. The old "Back to documents" link goes.
- **Title row** (flex, wrap, `gap: 8px 12px`, `align-items: center`):
  - `<h1 class="page-title">{{ typeLabel(doc.type) }}</h1>`.
  - The reference, when present (`.mono`, 14 px secondary).
  - `<app-status-pill>` from `documentStatus(doc)`.
  - Then, pushed right (`margin-left: auto`) and only `@if (canReverse())`: `<button mat-stroked-button type="button" (click)="reverse()" [disabled]="reversing()"><mat-icon>undo</mat-icon>Reverse</button>`.
- **Reverse error,** when set: `<p class="banner banner--bad" role="alert"><mat-icon>error</mat-icon><span>{{ e.message }} @if (e.linkId) { <a [routerLink]="['/documents', e.linkId]">{{ e.linkLabel }}</a> }</span></p>`.
- **Notice:**
  - `@if (doc.reversedByDocumentId)`: `<p class="banner banner--warn" role="status"><mat-icon>info</mat-icon><span>This posting was reversed. <a [routerLink]="['/documents', doc.reversedByDocumentId]">Open it</a></span></p>`.
  - `@if (doc.reversesDocumentId)`: the same in `banner--neutral`, reading "This reversal undoes an earlier posting. Open it" and linking to `reversesDocumentId`.
- **Meta grid** (`.meta`): `display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 180px), 1fr)); gap: 1px; background: var(--ims-border); border: 1px solid var(--ims-border); border-radius: 12px; overflow: hidden`.
  - Each cell is white with `padding: 14px 16px`: the label (12/16 500 muted) above the value (14/20).
  - Cells: Posted (`formatDate`), Posted by (`postedBy(doc)`), Reason (when present), Reference (when present, `.mono`), Notes (when present; let it span `grid-column: 1 / -1`, `white-space: pre-line`).
- **"Lines":** `<h2 class="section-title">Lines</h2>`, then a `.card` with `.table-scroll` around `<table class="data-table">` (`min-width: 640px`):
  - Item: name (500) above the SKU (`.mono`, 12/16 muted).
  - From → To: `.mono`, 13 px, with `title` holding the names.
  - Qty: `.num`, as `{q} {unit}`.
  - Unit cost: `.num`, "—" when null.
  - Total cost: `.num`.
  - `<tfoot>`: "Total" in the first cell (colspan 4) and `formatMoney(linesTotal())` in the last. `linesTotal` is a `computed` summing `doc.lines[].totalCost`, rounded to 4 dp for display (`Math.round(x * 10000) / 10000`); it is display-only (plan Assumptions).
- **Loading:** while `loading()` and there's no document and no error, a `.card` with 4 `.skeleton-row`s.
- Add `MatIconModule` and `StatusPill` to `imports`, and use `styleUrl: './document-detail.scss'` (under 2 kB: title row, meta grid, cell internals).

### `document-detail.spec.ts`
Keep every case; update texts and selectors only where the DOM changed:
- `:83` `h1` still contains "Received".
- `:95` hrefs still include both reversal links.
- `:102-103`: the negative checks become `not.toContain('This posting was reversed')` and `not.toContain('This reversal undoes')`.
- `:112` the first `a` is `/documents`.
- `:125` finds the button whose trimmed text **ends with** "Reverse", because the icon ligature text "undo" is in `textContent`. Better: give the button `data-testid="reverse"`, or match on `textContent.includes('Reverse')`.
- `:183-209` read `[role="alert"]`.

Add:
- A reversed document shows the warn notice with an "Open it" link to `/documents/<reversedBy>` and `role="status"`.
- The Lines footer shows the summed total.

## Do not
- Touch files outside the `files` list. If you must, stop and report why. In the e2e spec, change line 51 only.
- Change `canReverse()`, `reverse()`, `load()` or the idempotency-key handling.
- Add dependencies, or create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)/web"
npm run lint && npm run build && npm test -- --watch=false
grep -q "This posting was reversed." ../tests/e2e/specs/t14-stock-flow.spec.ts
```
- `/documents/<reversed id>` at 1280 px matches `desktop-06-document-detail-reversed.png`. At 375 px, `document.documentElement.scrollWidth <= 375` (e2e `post-stock-mobile.spec.ts` checks the detail page after posting), and the back link and the Reverse button measure ≥ 44 px tall from IMS-22's mobile block (plan D19; don't override it in `document-detail.scss`).
