---
type: plan
kind: feature
status: in-progress
repo: inventory-management-system
delivery: pr
base: develop
sprint: 2
epic: IMS-21
created: 2026-11-17
risk: medium
prompts: 12
graph_built: 2026-11-17 21:31:33
tags: [plan]
---
# Sprint 2 — UI redesign (neutral, responsive)

## Request
> @docs/design_handoff_inventory_redesign/  implement the design system. there are screenshots for you to reference in @docs/design_handoff_inventory_redesign/screenshots/

The handoff README (`docs/design_handoff_inventory_redesign/README.md`) is the spec for this request. The HTML prototype `Inventory App.dc.html` is the source of truth where the screenshots differ (README "Screenshots").

| # | Requirement |
|---|---|
| R1 | Design system: the README's tokens (colours, spacing, radius, shadows), IBM Plex Sans/Mono, Material theme switched to the neutral palette through `--mat-sys-*` overrides, restyled Material buttons, inputs (label above), selects, switch and paginator, a shared AG Grid theme, the shared table, banner and skeleton styles, and one shared `StatusPill` (`neutral`/`good`/`warn`/`bad`). |
| R2 | App shell: a 248 px sidebar at ≥ 760 px (brand, nav groups Inventory / Post stock / Admin, user footer with sign-out); below 760 px, a 56 px top bar with a menu button, a "+ Post" link and a drawer. Role gating is unchanged. Items and Documents stay active on their detail routes. Scroll to the top on route change. |
| R3 | Stock on hand (`/stock`): page header and subtitle; one card holding the filter bar (Location select, "Total value" headline), the table (Item name + mono SKU, Location, Status pill, Qty, Unit, Avg cost, Value, Total footer) and the pagination row; stacked rows below 760 px; stock status derived from `onHand`. |
| R4 | Items (`/items`) as a card with a search and Show-inactive filter bar and wrapping rows with per-row Admin actions; Item detail (`/items/:id`) with a back link, a title and status pill, a meta line, and "Stock by location" and "History" tables that scroll sideways. |
| R5 | Locations (`/locations`) as a card list: code badge, name, pills (System, Inactive, Negative stock allowed), and the Admin switch, Edit and Deactivate wrapping below on mobile. |
| R6 | Documents (`/documents`): grid filter bar (Type, From, To), a table with type icons, mono references and a status pill (Posted / Reversed / Reverses another), and stacked rows below 760 px. Document detail (`/documents/:id`): back link, title row with reference, pill and Reverse button, a reversed/reversal notice banner, the meta grid, and a Lines table with a footer total. |
| R7 | Post stock (`/documents/new/:type`): titles and subtitles per type, a "Details" card, a "Lines" card with wrapping line rows (Item, Add/Remove segment, Quantity · unit, Unit cost · ₱, a remove icon button), a per-line on-hand hint and negative warning, a dashed "+ Add line", a sticky action bar with the running total, Cancel and Post, and the error banner in the `bad` tone. |
| R8 | Ledger health (`/admin/ledger`): header actions with icons, an overall healthy/failing banner, an invariant list card with Passed/Failed pills, failed rows expanding a nested table, and the rebuild result in the same nested-table style. |
| R9 | No new features or data. Every field, label, column, filter, permission rule, validation message, snackbar and loading signal keeps its current behaviour (README "Overview", "Interactions & Behavior"). Existing behaviour tests keep passing; only their DOM selectors change. |
| R10 | Phones: 375–430 px with no sideways page scroll, touch targets ≥ 42–44 px, fluid page padding with `clamp()`. |

## Context used
- Handoff: `docs/design_handoff_inventory_redesign/README.md` (all sections): tokens, per-screen specs, components, responsive rules, the source mapping to `web/src/app/*`. Prototype `Inventory App.dc.html:795-823`: the post-bar total rule (cost lines `q × c`, other lines `q × average cost`); `:851`: the healthy sub-line uses a balance count the health API doesn't return.
- Screenshots: `desktop-01-stock`, `desktop-07-receive-stock`, `desktop-09-ledger-health`, `mobile-stock` reviewed.
- Spec §10 (`docs/Inventory System — Sprint 1 Spec.md:594-626`): `/stock` is "AG Grid … totals row from the API's totals … negative balances in red"; `/items` is "Grid with search"; post-stock needs "item autocomplete by SKU or name" and a warning before submit when a line exceeds on-hand; 375 px forms; hide buttons the role can't use.
- TASKS.md IMS-16 Downstream: "Renaming … a UI label used by the specs breaks the suite." IMS-14 Downstream: the switch reads "Allow negative stock" with confirm "Allow". IMS-11 Downstream: `PostedBy` `Guid.Empty` shows as "System". IMS-5 Downstream: reuse `paged.ts`, `problemFieldErrors`, `confirm-dialog.ts`.
- e2e selectors (`tests/e2e/support/app.ts:19-104`, `specs/*.ts`): combobox names `Location`, `Item`; labels `Reference`, `Quantity`, `Unit cost`, `Email`, `Password`; buttons `Post`, `Reverse`, `Rebuild balances`, `Sign in`; link `Ledger health`; text `All checks passed`, `Passed` (exact, ×5), `Nothing to fix.`, `Reversed by`; stock grid `role=row` filtered by SKU, count 1, with `[col-id="onHand"|"averageCost"|"stockValue"]`; `document.documentElement.scrollWidth ≤ 375` on the post form and document detail.
- Unit specs that query the DOM: `app.spec.ts` (`mat-toolbar`, `a[href]`), `items.spec.ts` (selection-gated Edit/Deactivate), `locations.spec.ts` (`mat-chip`, `mat-slide-toggle`), `ledger-health.spec.ts` (`.invariant`, `.marker.pass/.fail`, `.rebuild tbody tr`), `document-detail.spec.ts:95-125,183-209`, `item-detail.spec.ts:130,155`, `post-stock.spec.ts:145,188`.
- `web/angular.json`: `anyComponentStyle` budget 4 kB warning / 8 kB error, so shared styles go global.
- `web/src/index.html`: Roboto and Material Symbols are already loaded from Google Fonts, so IBM Plex follows the same path. `web/nginx.conf.template` sets no CSP.
- Graph (`graphify update .` then `affected`): `StockService` → `stock.ts`, `post-stock.ts` and their specs; `StockService.onHand` has one caller (`post-stock.ts:419`). `shared/format.ts` is imported by 24 files, so its additions must be additive only.
- Code: `app.html:1-39` (current toolbar, `Documents` link uses `exact: true`), `stock.ts:35-68` (column defs, `NEGATIVE_CELL`), `documents.ts:75-105`, `items.ts:51-154` (selection model), `locations.html:15-31` (pill rules), `document-detail.ts:107-112` (`canReverse`), `post-stock.ts:166-173` (`onHandText`), `posting-form.ts:31` (`POSTING_TITLES`), `ledger-health.ts:16-22` (`INVARIANT_LABELS`), `ledger.service.ts:26-30` (health has no balance count), `items.service.ts:30-48` (`ItemMovement` has no reference).

## Options
The one real choice was how to deliver it.

| Option | Cost | Time | Risk | Maintenance | Compliance |
|---|---|---|---|---|---|
| A. One PR for the whole redesign | low overhead | 1 long build | high: one review of ~45 files; an e2e failure blocks everything | same | none |
| B. Foundation + shell PR, then one PR per screen group | 5 tickets | same total, reviewable chunks | low: each PR keeps e2e green on its own | same | none |

**Recommendation:** B: IMS-22 lays the tokens, primitives and shell; IMS-23–IMS-26 restyle screen groups and each depends only on IMS-22. **Chosen:** B. This is the CTO default; no pick was needed beyond the clarify round.

## Decisions
- **D1 (user):** `/items` becomes the design's row list with per-row Admin actions, and AG Grid and row selection leave Items. IMS-24 amends spec §10's `/items` row and logs the deviation in §15, the way IMS-18 logged its own.
- **D2 (user):** the post-stock Item field stays a `mat-autocomplete`, restyled. Spec §10 requires it, the e2e helper types into the `Item` combobox, and a native select doesn't scale.
- **D3 (user):** the Item-detail History "Document" column shows the type label only (no `· REF`). `GET /items/{id}/movements` has no reference, and the README promises no new data.
- **D4:** Stock and Documents keep AG Grid on desktop, themed through one shared `appGridTheme = themeQuartz.withParams({...})`. Spec §10 mandates AG Grid for `/stock`, and e2e reads its `role=row` and `col-id`s. Column `field`s `onHand`, `averageCost` and `stockValue` keep their names. The Stock totals stay a pinned bottom row ("Total"), and the filter-bar headline repeats them.
- **D5:** the mobile stacked lists render with `@if (isNarrow())`, not CSS hiding. Only one of grid or list is ever in the DOM, so e2e's row count stays 1 and nothing is duplicated.
- **D6 (label above the field):** a visible `<label class="field-label" [for]="id">` sits above a `mat-form-field appearance="outline" subscriptSizing="dynamic"` that has no `mat-label`. `matInput` gets the matching `id`, and `mat-select` gets `aria-labelledby` set to the label's id. This keeps the accessible names `Location`, `From`, `To`, `Reason`, `Reference`, `Notes`, `Item`, `Quantity`, `Unit cost` that e2e uses. Playwright's name match is a substring match, so a hint inside the label (`Quantity · pc`, `Unit cost · ₱`, `Notes · optional`) is fine. Login and the dialogs keep floating outline labels; they only pick up the theme.
- **D7:** shared primitives live in `web/src/styles.scss`, as Material override mixins and global classes, because `angular.json` caps component styles at 4 kB (warning) and 8 kB (error). After IMS-22, IMS-23–IMS-26 never edit `styles.scss`: anything screen-specific goes in that component's SCSS. This also avoids merge conflicts between the screen units.
- **D8:** tokens are CSS custom properties `--ims-*` on `:root`. Material system variables are overridden to them (`--mat-sys-primary: #1C1B19`, `--mat-sys-surface`, `--mat-sys-outline-variant`, `--mat-sys-error` → bad fg, …). The status-tone colours use oklch values with hex fallbacks.
- **D9:** icons are `mat-icon` with Material Symbols Outlined as the default font-set class (`MatIconRegistry.setDefaultFontSetClass('material-symbols-outlined')`), with `font-variation-settings: 'opsz' 20, 'wght' 350`. `index.html` loads the `css2` URL with axes. `mat-icon` is `aria-hidden` by default, so every icon-only button carries an `aria-label`.
- **D10:** the mobile top bar's "+ Post" is an `<a mat-stroked-button routerLink="/documents/new/receipt">`, a link. A `<button>` named "Post" would collide with e2e `getByRole('button', { name: 'Post' })` at 375 px.
- **D11:** AG Grid custom cells use Angular component renderers (`ICellRendererAngularComp`), never HTML-string renderers. Item names, SKUs and references are user input, and string renderers set `innerHTML` (XSS).
- **D12:** `stockStatus(onHand)` and `documentStatus(doc)` are pure functions in `shared/format.ts` that return `{ tone, label }` (README: labels come from `format.ts`). The additions are additive only.
- **D13:** Ledger keeps the ids `INV-1`…`INV-5` and the current `INVARIANT_LABELS`: spec §10 and the API use them, and the prototype's `L1` names are sample data. The healthy sub-line reads "Ran {n} checks." because the health response has no balance count.
- **D14:** the post-bar total follows the prototype. Receipt lines and adjustment Add lines add `quantity × unitCost`. Issue, transfer and adjustment Remove lines add `quantity × averageCost` at the source location (0 when it is unknown). Receipt and adjustment are labelled "Total cost"; issue and transfer, "Value at average cost". It is display-only; the server computes the real costs. The average cost comes from a new `StockService.balance(itemId, locationId)` that returns `{ onHand, averageCost }` from the same `GET /api/stock` call. `onHand()` stays and delegates to it.
- **D15:** e2e `t14-stock-flow.spec.ts:51` `getByText('Reversed by')` becomes `getByText('This posting was reversed.')`, changed in IMS-25, the same PR as the text.
- **D16:** the shell has exactly one nav. The same `<aside>` is the sticky sidebar on desktop and a fixed drawer on mobile, and it isn't rendered on mobile while the drawer is closed. Otherwise e2e `getByRole('link', { name: 'Ledger health' })` matches twice, and the Staff `toHaveCount(0)` check stays valid.
- **D17:** IMS-22's PR also commits `docs/design_handoff_inventory_redesign/**`, so every later prompt can cite the README and the prototype from `develop`.
- **D18:** Documents nav is active on `/documents` and `/documents/:id` but not on `/documents/new/*`, whose entries are the Post stock links. Items is active on `/items` and `/items/:id`.
- **D19 (audit, R10):** the handoff's small control sizes (`.btn-sm` 32, `.btn-md` 36, `.btn-icon` 32×32, `.btn-dashed` 36, `.back-link` 28, paginator buttons 32×32) are desktop sizes. Prompt 01 adds one `@media (max-width: 759.98px)` block in `styles.scss` that raises each of them to at least 44 px (`min-height: 44px`; `.btn-icon` and the paginator buttons 44×44), so screens get R10 from the shared classes with no per-screen sizing. `density: 0` stays, and no rule hides `.mat-mdc-button-touch-target`. The mobile visuals grow slightly past the mockups; README "Responsive" wins over the screenshots on this point.

**Assumptions:**
- Between IMS-22 and the screen units, the screens keep their own `:host { padding: 16px }` inside the shell's new padded main area. That looks loose but isn't broken, and each screen unit removes it. This is safe because each unit deploys on its own and nothing overlaps.
- Desktop AG Grid keeps its built-in loading overlay, which already sits inside the table card. The README's skeleton rows (`#F2F1EF` bars, 12 px) apply to the HTML tables and stacked lists. The overlay is not page-level, so the intent holds.
- The Stock subtitle's `{m}` is the count of locations in the filter list (`locations().length`), and `{n}` is `total()`. The API has no distinct-location count, and that list is what the user can filter by. Singular forms: "1 balance", "1 location".
- The Items subtitle reads "{n} active items", or "{n} items" while Show inactive is on, with `{n}` = `total()`.
- Post-stock "Cancel" navigates to `/stock`, the landing page. Nothing is saved, so no confirm is needed.
- The document-detail Lines footer total is the sum of `line.totalCost`. It is display-only, from values the API already returns.
- Adjustment totals add Remove lines as positive amounts, as the prototype does, so a mixed adjustment shows gross movement, not net. Audit may want net; changing it is a one-line change in `postingTotal`.
- Sprint 1 isn't rolled up to `0.1.0` yet (CHANGELOG is still `[Unreleased]`). That roll-up is bookkeeping on `develop` and doesn't block this plan, but Sprint 2 should close at `0.2.0` after it.

**Non-goals:** dark mode; redesigning login, the item and location dialogs, or the confirm dialog (they only pick up the theme); any API, DTO or database change; new filters, columns or permissions; changing validation, problem or snackbar text; replacing AG Grid on Stock or Documents; new e2e scenarios beyond keeping the existing four green; touching `deploy/` (the nginx image rebuilds from `web/` unchanged).

## Change list
| File | Action | Why | Req | Prompt |
|---|---|---|---|---|
| `docs/design_handoff_inventory_redesign/**` | C (commit as is) | Prompts cite it from `develop` (D17) | R1 | 01 |
| `web/src/index.html` | M | IBM Plex Sans/Mono replace Roboto; Material Symbols `css2` with axes | R1 | 01 |
| `web/src/styles.scss` | M | Neutral `mat.theme`, `--mat-sys-*` overrides, `--ims-*` tokens, Material component overrides, global primitives (page, header, card, filter bar, data table, num, mono, field label, banner, skeleton, back link, stacked row); `.ag-cell.stock-negative` → bad fg | R1, R10 | 01 |
| `web/src/app/shared/grid-theme.ts` | C | `appGridTheme` (themeQuartz.withParams) shared by Stock and Documents (D4) | R1 | 01 |
| `web/src/app/shared/format.ts` | M | Add `StatusTone`, `stockStatus`, `documentStatus` (D12) | R1, R3, R6 | 02 |
| `web/src/app/shared/format.spec.ts` | M | Tests for the two status functions | R1 | 02 |
| `web/src/app/shared/status-pill.ts` | C | `StatusPill` component (`tone`, `label` inputs) | R1 | 02 |
| `web/src/app/shared/status-pill.spec.ts` | C | Renders label, tone class, dot | R1 | 02 |
| `web/src/app/shared/status-pill-cell.ts` | C | AG Grid component renderer wrapping `StatusPill` (D11) | R3, R6 | 02 |
| `web/src/app/shared/layout.ts` | C | `injectIsNarrow()` signal from `BreakpointObserver` `(max-width: 759.98px)` | R2, R3, R6 | 02 |
| `web/src/app/app.html` | M | Sidebar / top bar / drawer shell replacing `mat-toolbar` | R2 | 03 |
| `web/src/app/app.scss` | M | Shell layout and nav styles | R2, R10 | 03 |
| `web/src/app/app.ts` | M | `drawerOpen`, `isNarrow`, initials, nav-active helpers, close on navigation | R2 | 03 |
| `web/src/app/app.config.ts` | M | `withInMemoryScrolling({ scrollPositionRestoration: 'top' })`; default form-field options (outline, dynamic subscript); default icon font set (D9) | R1, R2 | 03 |
| `web/src/app/app.spec.ts` | M | Replace `mat-toolbar` queries; nav groups, role gating, sign-out, footer | R2, R9 | 03 |
| `web/src/app/stock/stock.ts` | M | Theme, cell renderers (Item, Location, Status), `isNarrow`, subtitle, status per row | R3 | 04 |
| `web/src/app/stock/stock.html` | M | Header, card, filter bar with total, grid / stacked list, paginator | R3 | 04 |
| `web/src/app/stock/stock.scss` | M | Screen-specific layout only | R3 | 04 |
| `web/src/app/stock/stock.spec.ts` | M | Status derivation per row, mobile list render, subtitle | R3, R9 | 04 |
| `web/src/app/documents/documents.ts` | M | Theme, Type and Status renderers, `isNarrow`; inline styles move out | R6 | 05 |
| `web/src/app/documents/documents.html` | M | Header, card, grid filter bar, grid / stacked list | R6 | 05 |
| `web/src/app/documents/documents.scss` | C | Styles moved from inline `styles:` | R6 | 05 |
| `web/src/app/documents/documents.spec.ts` | M | Status column, mobile list | R6, R9 | 05 |
| `web/src/app/items/items.ts` | M | Drop AG Grid and selection; `deactivate(item)` per row (D1) | R4 | 06 |
| `web/src/app/items/items.html` | M | Card, filter bar, row list, per-row Edit / Deactivate | R4 | 06 |
| `web/src/app/items/items.scss` | M | Row layout | R4 | 06 |
| `web/src/app/items/items.spec.ts` | M | Per-row actions replace selection-gated buttons | R4, R9 | 06 |
| `docs/Inventory System — Sprint 1 Spec.md` | M | §10 `/items` row: "List with search"; §15 log line for D1 | R4 | 06 |
| `web/src/app/items/item-detail.ts` | M | `styleUrl`, total on hand, status | R4 | 07 |
| `web/src/app/items/item-detail.html` | M | Back link, title + pill, meta line, two table cards | R4 | 07 |
| `web/src/app/items/item-detail.scss` | C | Styles moved from inline | R4 | 07 |
| `web/src/app/items/item-detail.spec.ts` | M | Back link, pill, meta | R4, R9 | 07 |
| `web/src/app/locations/locations.ts` | M | Drop `MatChipsModule`/`MatListModule`; add `StatusPill`; subtitle | R5 | 08 |
| `web/src/app/locations/locations.html` | M | Header with subtitle, card list, badge, pills, actions | R5 | 08 |
| `web/src/app/locations/locations.scss` | M | Row layout | R5 | 08 |
| `web/src/app/locations/locations.spec.ts` | M | `mat-chip` → `app-status-pill` | R5, R9 | 08 |
| `web/src/app/documents/document-detail.ts` | M | `styleUrl`, status, lines total, icons | R6 | 09 |
| `web/src/app/documents/document-detail.html` | M | Back link, title row, notice banner, meta grid, lines card | R6 | 09 |
| `web/src/app/documents/document-detail.scss` | C | Styles moved from inline | R6 | 09 |
| `web/src/app/documents/document-detail.spec.ts` | M | Banner text, back link, hrefs | R6, R9 | 09 |
| `tests/e2e/specs/t14-stock-flow.spec.ts` | M | `Reversed by` → `This posting was reversed.` (D15) | R9 | 09 |
| `web/src/app/ledger/ledger-health.ts` | M | Icons, `StatusPill`, sub-line | R8 | 10 |
| `web/src/app/ledger/ledger-health.html` | M | Header, banner, invariant list card, nested tables | R8 | 10 |
| `web/src/app/ledger/ledger-health.scss` | M | Screen layout | R8 | 10 |
| `web/src/app/ledger/ledger-health.spec.ts` | M | New selectors; same behaviour | R8, R9 | 10 |
| `web/src/app/documents/posting-form.ts` | M | `POSTING_SUBTITLES`, `postingTotal`, `postingTotalLabel` (reuse the existing `isOutflow`, `usesUnitCost`) | R7 | 11 |
| `web/src/app/documents/posting-form.spec.ts` | M | Tests for the additions | R7 | 11 |
| `web/src/app/stock/stock.service.ts` | M | `balance(itemId, locationId)`; `onHand` delegates (D14) | R7 | 11 |
| `web/src/app/stock/stock.service.spec.ts` | M | `balance` test | R7 | 11 |
| `web/src/app/documents/post-stock.ts` | M | Average cost per line, total, hint text, Cancel, location code for hints | R7 | 12 |
| `web/src/app/documents/post-stock.html` | M | Details card, Lines card, line rows, hints, sticky bar, banner | R7 | 12 |
| `web/src/app/documents/post-stock.scss` | M | Line row, segment, sticky bar | R7, R10 | 12 |
| `web/src/app/documents/post-stock.spec.ts` | M | Hint text, total, Cancel, remove-line icon button | R7, R9 | 12 |

Review constraints check: no backend, SQL, money-type, ledger, TenantId, date-in-request or package changes. `@angular/cdk/layout` (`BreakpointObserver`) and `mat-icon` come from already-allowed packages. No package is added. All pass.

## Blast radius
**Risk:** medium. It touches no high-stakes path, shared .NET code or API contract, but it rewrites every screen's DOM and the e2e suite (a required CI check) reads that DOM.

| Affected | Kind | Via | Breaks? | Mitigation |
|---|---|---|---|---|
| `tests/e2e/support/app.ts` `choose('Location'…)`, `getByRole('combobox', {name:'Item'})`, `getByLabel('Reference'|'Quantity'|'Unit cost')` | Non-graph | accessible names | Yes, if the label-above pattern drops names | D6: external `<label for>` + `aria-labelledby` on `mat-select`; prompts 04, 12 assert the names in unit specs |
| `tests/e2e/support/app.ts:92` `getByRole('button',{name:'Post'})` | Non-graph | button name | Yes, if the top bar "+ Post" is a button | D10: it is a link; prompt 03 spec asserts `a[href="/documents/new/receipt"]` in the top bar |
| `tests/e2e/support/app.ts:99-104` `stockRow` | Non-graph | AG Grid `role=row`, `col-id` | Yes, if the grid is replaced or duplicated | D4, D5: grid kept on desktop with the same fields; one of grid or list in the DOM |
| `t14-stock-flow.spec.ts:51` `getByText('Reversed by')` | Non-graph | banner text | **Yes** | D15: updated in prompt 09 (same PR) |
| `ledger-health.spec.ts` (e2e) `getByText('Passed',{exact:true})` ×5, `All checks passed`, `Nothing to fix.` | Non-graph | pill text, banner text | Yes, if the pill splits or repeats the text | Prompt 10: one text node per pill label; keep both strings verbatim |
| e2e `getByRole('link',{name:'Ledger health'})` (Admin click, Staff count 0) | Non-graph | nav | Yes, if two navs render | D16: one `<aside>` |
| `post-stock-mobile.spec.ts` scrollWidth ≤ 375 (form and document detail) | Non-graph | layout | Yes, if a table or drawer overflows | Tables in `overflow-x: auto` wrappers; drawer not rendered when closed; prompts 09, 12 Done-when |
| `app.spec.ts` | Direct | `mat-toolbar` queries | Yes | Prompt 03 rewrites them |
| `items.spec.ts` | Direct | selection-gated buttons | Yes (D1) | Prompt 06 |
| `locations.spec.ts:83-141` | Direct | `mat-chip`, `mat-slide-toggle` | Chips yes; toggle no (kept) | Prompt 08 |
| `ledger-health.spec.ts` (unit) | Direct | `.invariant`, `.marker.*` | Yes | Prompt 10 keeps `.invariant` and adds `data-passed`; updates selectors |
| `document-detail.spec.ts:83-125,183-209` | Direct | `h1`, first `a`, `[role=alert]` | Partly (first `a` becomes the back link `/documents`, which is what `:112` expects already) | Prompt 09 checks each |
| `item-detail.spec.ts:130,155` | Direct | `a` hrefs | No: the back link is `/stock`, history links stay | Prompt 07 verifies |
| `post-stock.spec.ts:145,188` | Direct | first `a`, remove button | Maybe | Prompt 12 updates |
| `shared/format.ts` importers (24 files) | Direct | additive exports | No | Additive only (D12) |
| `StockService.onHand` (`post-stock.ts:419`, its spec) | Direct | signature kept | No | `onHand` delegates to `balance` (D14) |
| Login, item dialog, location dialog, confirm dialog | Transitive | theme, default form-field options | Visual only | Outline was already their appearance (login); their specs query labels, not styles. Prompt 03 runs the full `npm test` |
| ProblemDetails map (`problem-messages.ts`) | Non-graph (hidden coupling) | untouched | No | Not in the change list |
| `web/Dockerfile`, `deploy/**`, Bicep | Non-graph | build output only | No | No env, port or nginx change |
| Component style budget (4 kB warn / 8 kB error) | Non-graph | `angular.json` | Yes, if styles stay per-component | D7; each prompt's Done-when runs `npm run build` |
| R10 touch targets vs the handoff's 28–36 px controls | Non-graph | shared button, back-link and paginator classes | Yes, on phones, if sized as on desktop | D19: one mobile block in prompt 01; prompts 03, 06–09 check ≥ 44 px at 375 px |

**Known risks in the touched area:** IMS-16 records that a renamed UI label breaks the e2e suite, which is why every label-bearing prompt states the names it must keep. The suite can't run on arm64 Macs and needs `E2E_PASSWORD`, so CI's `e2e` job is the gate for each PR.

## Units
| Unit | Title | Category | Prompts | Depends | High-stakes | Acceptance criteria |
|---|---|---|---|---|---|---|
| IMS-22 | Design foundation and app shell | Changed | 01, 02, 03 | — | no | R1: tokens, fonts, theme overrides, `appGridTheme`, `StatusPill` and its cell renderer, `stockStatus`/`documentStatus` are in place and tested. R2: sidebar at ≥ 760 px and top bar + drawer below, nav groups and role gating as specified, Items/Documents active on detail routes, scroll to top. R9: `npm test` and CI `e2e` green. |
| IMS-23 | Stock and Documents lists | Changed | 04, 05 | IMS-22 | no | R3: Stock card with filter bar and total headline, themed grid with status pills, pinned Total, stacked rows < 760 px. R6 (list): Documents grid filter bar, type icons, status pills, stacked rows < 760 px. R9: e2e `stockRow` still finds one row with the three `col-id`s. R10: no sideways scroll at 375 px. |
| IMS-24 | Items, Item detail and Locations | Changed | 06, 07, 08 | IMS-22 | no | R4: Items rows with per-row Admin actions (Staff sees none); Item detail back link, title + pill, meta line, two sideways-scrolling table cards. R5: Locations card list with badge, pills and Admin actions. Spec §10/§15 amended (D1). R9: unit specs green. |
| IMS-25 | Document detail and Ledger health | Changed | 09, 10 | IMS-22 | no | R6 (detail): back link, title row, notice banner, meta grid, Lines card with total, Reverse only under `canReverse()`. R8: header actions, overall banner, invariant list with pills and nested failure tables, rebuild result. R9: e2e T14 and ledger specs green with D15. |
| IMS-26 | Post stock form | Changed | 11, 12 | IMS-22 | no | R7: titles and subtitles, Details and Lines cards, line rows, on-hand hint and negative warning, dashed Add line, sticky bar with the D14 total, Cancel and Post, bad-tone banner. R9: validation, idempotency and over-on-hand confirm unchanged; e2e `postDocument` and the 375 px spec green. R10. |

## Prompts
| # | Title | Unit | Model | Effort | Depends | Group | Files |
|---|---|---|---|---|---|---|---|
| 01 | Tokens, fonts, Material theme, global primitives | IMS-22 | sonnet | high | — | A | `index.html`, `styles.scss`, `shared/grid-theme.ts`, handoff docs |
| 02 | Status helpers, StatusPill, cell renderer, isNarrow | IMS-22 | sonnet | medium | 01 | B | `format.ts`, `format.spec.ts`, `status-pill.ts`, `status-pill.spec.ts`, `status-pill-cell.ts`, `layout.ts` |
| 03 | App shell: sidebar, top bar, drawer | IMS-22 | sonnet | high | 01, 02 | C | `app.html`, `app.scss`, `app.ts`, `app.config.ts`, `app.spec.ts` |
| 04 | Stock on hand screen | IMS-23 | sonnet | high | IMS-22 | A | `stock.ts`, `stock.html`, `stock.scss`, `stock.spec.ts` |
| 05 | Documents list screen | IMS-23 | sonnet | medium | 04 | B | `documents.ts`, `documents.html`, `documents.scss`, `documents.spec.ts` |
| 06 | Items list with per-row actions | IMS-24 | sonnet | medium | IMS-22 | A | `items.ts`, `items.html`, `items.scss`, `items.spec.ts`, spec doc |
| 07 | Item detail screen | IMS-24 | sonnet | medium | 06 | B | `item-detail.ts`, `item-detail.html`, `item-detail.scss`, `item-detail.spec.ts` |
| 08 | Locations screen | IMS-24 | sonnet | medium | 07 | C | `locations.ts`, `locations.html`, `locations.scss`, `locations.spec.ts` |
| 09 | Document detail screen | IMS-25 | sonnet | medium | IMS-22 | A | `document-detail.ts`, `.html`, `.scss`, `.spec.ts`, e2e `t14-stock-flow.spec.ts` |
| 10 | Ledger health screen | IMS-25 | sonnet | medium | 09 | B | `ledger-health.ts`, `.html`, `.scss`, `.spec.ts` |
| 11 | Post-stock total, subtitles, balance lookup | IMS-26 | sonnet | medium | IMS-22 | A | `posting-form.ts`, `posting-form.spec.ts`, `stock.service.ts`, `stock.service.spec.ts` |
| 12 | Post-stock form layout | IMS-26 | sonnet | high | 11 | B | `post-stock.ts`, `post-stock.html`, `post-stock.scss`, `post-stock.spec.ts` |

No `opus` prompts: nothing touches a high-stakes path, shared .NET code, auth or money logic. Prompts 01, 03, 04 and 12 are `high` effort because they carry the e2e-visible contracts (accessible names, one nav, grid fields) and the Material override API.

Within a unit, prompts run in order on one branch (one `web` package and one lockfile, so each is in its own group). Across units, IMS-23–IMS-26 depend only on IMS-22 and touch disjoint files. Run them in any order after IMS-22 merges.

## End-to-end verification
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build && npm test -- --watch=false
# R9/R10 and the e2e contracts (CI job `e2e` runs this on every PR; locally it needs Docker and amd64):
cd ../tests/e2e && npx playwright test   # with E2E_PASSWORD exported as README "End-to-end tests" describes
# R1–R8 visual: run the AppHost, open http://localhost:4200 at 1280 px and at 390 px (DevTools device mode),
# sign in as admin@demo.test and staff@demo.test, and compare each route with
# docs/design_handoff_inventory_redesign/screenshots/* and the prototype (`Inventory App.dc.html?screen=…`).
# R10: at 375 px, `document.documentElement.scrollWidth <= 375` on every route (DevTools console), and every
# button, link-button, back link and paginator button measures >= 44 px tall (icon buttons >= 44 px wide too) (D19).
```

## Rollback
Frontend-only, with no data or contract change. Revert the unit's merge commit on `develop`. IMS-23–IMS-26 depend on IMS-22's `styles.scss` classes, `StatusPill`, `appGridTheme` and `injectIsNarrow`, so reverting IMS-22 needs every merged screen unit reverted first, newest first.

## Log
- 2026-11-17: planned
- 2026-11-17: audited, 1 fix applied (R10 mobile touch targets, D19)
- 2026-11-17: applied Codex audit (1 applied, 0 disputed)
- 2026-11-17: build started on IMS-22; rollback point 09cdc2aabc6aefb770ff98a056fee9d7fc2a56be
- 2026-11-17: 01 done (sonnet, 0 retries): tokens, fonts, Material overrides, global classes, appGridTheme; lint/build/183 tests green
- 2026-11-17: 02 done (sonnet, 0 retries): stockStatus/documentStatus, StatusPill, StatusPillCell, injectIsNarrow; 193 tests green
- 2026-11-17: 03 done (sonnet, 0 retries): sidebar/top bar/drawer shell, scroll-to-top, outline form-field default, Material Symbols default; 199 tests green. Deviation: backdrop is a <button aria-label="Close navigation"> (template lint). Note: initial bundle 410 -> 557 kB (>500 kB warning budget); MAT_FORM_FIELD_DEFAULT_OPTIONS in app.config.ts accounts for ~105 kB eager
- 2026-11-18: build started on IMS-23; rollback point 04534f0d0f98e55beee56fad665d777d5e264860
- 2026-11-18: 04 done (sonnet, 0 retries): Stock restyled (header, card, themed grid, mobile list, status pill); 204 tests green. Deviations: Item column keeps SKU · Name valueGetter alongside the renderer; one eslint-disable on the label-has-associated-control rule (label-above pattern)
- 2026-11-18: 05 done (sonnet, 0 retries): Documents restyled (filter grid, type icons, status pill, mobile rows); 208 tests green
- 2026-11-18: IMS-23 merged; fix commit for e2e column virtualisation (flex columns, suppressColumnVirtualisation on Stock)
- 2026-11-18: build started on IMS-24; rollback point 154fd1a51f33c61c142f65d7a40f8afc02a6e05f
- 2026-11-18: 06 done (sonnet, 0 retries): Items card list with per-row Admin actions, spec §10/§15 amended; lint/build/210 tests green
- 2026-11-18: 07 done (sonnet, 0 retries): Item detail restyled (back link, title pill, meta line, scrolling table cards); lint/build/212 tests green
- 2026-11-18: 08 done (sonnet, 0 retries): Locations card list with badge, pills, Admin actions; lint/build/212 tests green
- 2026-11-18: IMS-24 merged
- 2026-11-18: build started on IMS-25; rollback point e1f1864e3c0e00e96a99a076b08b3e147d658526
- 2026-11-18: 09 done (sonnet, 0 retries): Document detail restyled (back link, title pill, notices, meta grid, Lines footer total); lint/214 tests green
- 2026-11-18: 10 done (sonnet, 0 retries): Ledger health restyled (overall banner, invariant list with pills, nested failure tables, rebuild card); dotnet/lint/build/214 tests green. Note: used --ims-secondary/--ims-muted tokens
- 2026-11-18: IMS-25 merged
- 2026-11-18: build started on IMS-26; rollback point 7b5b28307707b66741dfe9065f4138910c11950c
- 2026-11-18: 11 done (sonnet, 0 retries): POSTING_SUBTITLES, postingTotal/postingTotalLabel, StockService.balance; lint/build/223 tests green
- 2026-11-18: 12 done (sonnet, 0 retries): Post stock restyled (header, banner, Details/Lines cards, line hints, sticky total bar); dotnet 582/lint/build/227 tests green. Fixed by lead: host 'page page--narrow' + page-header wrapper (agent missed). Not run locally: Playwright e2e and screenshot comparison (CI e2e covers e2e)
