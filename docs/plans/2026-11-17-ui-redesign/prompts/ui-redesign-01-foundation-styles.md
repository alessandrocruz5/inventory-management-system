---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 01
unit: IMS-22
status: todo
model: sonnet
effort: high
depends_on: []
parallel_group: A
files:
  create:
    - web/src/app/shared/grid-theme.ts
    - docs/design_handoff_inventory_redesign/README.md
    - docs/design_handoff_inventory_redesign/Inventory App.dc.html
    - docs/design_handoff_inventory_redesign/Inventory Mockups.dc.html
    - docs/design_handoff_inventory_redesign/support.js
    - docs/design_handoff_inventory_redesign/screenshots/*.png
  modify:
    - web/src/index.html
    - web/src/styles.scss
  delete: []
---
# 01 · Tokens, fonts, Material theme, global primitives

## Task
Lay the design-system foundation for the redesign. Put the handoff's tokens, fonts, neutral Material theme, Material component overrides and the shared global CSS classes in `web/src/styles.scss`, load the fonts in `web/src/index.html`, and add a shared AG Grid theme in `web/src/app/shared/grid-theme.ts`. Don't change any component. Every later prompt builds on the class names defined here, so define **exactly** the class API below.

The handoff folder `docs/design_handoff_inventory_redesign/` already exists, untracked, in the working tree. Don't modify it. It is listed in `files` so it is committed with this unit (plan D17).

## Context
- Repo root: `git rev-parse --show-toplevel`. Work on the current branch (the unit's ticket key). Stop if `git branch --show-current` prints `develop` or `main`.
- **Source of truth:** read `docs/design_handoff_inventory_redesign/README.md` in full, especially "Components" and "Design Tokens". `Inventory App.dc.html` has inline styles for every element if you need an exact value. The screenshots are in `screenshots/`.
- Angular 22 + Angular Material 22 (`mat.theme()`, M3 system variables `--mat-sys-*`), AG Grid 36.2.0 (exact-pinned). No new npm package.
- `web/angular.json` caps **component** styles at 4 kB (warning) and 8 kB (error), so every shared style goes in the global `styles.scss` (plan D7). After this unit, no other prompt edits `styles.scss`, so anything a later screen needs globally must be here. That includes styles that reach AG Grid's DOM, which component styles can't.

### `web/src/index.html`
- Replace the Roboto `<link>` with IBM Plex: `https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=IBM+Plex+Sans:wght@400;500;600&display=swap`.
- Replace the Material Symbols link with the axes version (copy it from the prototype's `<helmet>`): `https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:opsz,wght,FILL,GRAD@20..24,300..400,0,0`.
- Keep the two `preconnect` links.

### `web/src/styles.scss`
1. **Theme** (plan D8): keep `@include mat.theme(...)` on `html`. Set `typography` to IBM Plex Sans (`(plain-family: 'IBM Plex Sans', brand-family: 'IBM Plex Sans', regular-weight: 400, medium-weight: 500, bold-weight: 600)`) and keep `density: 0`. Any predefined palette may stay in `color`, because you override the system colours right after with `@include mat.theme-overrides((...))` (check that the mixin exists in `node_modules/@angular/material/_index.scss`; if it doesn't, declare the `--mat-sys-*` variables directly on `html`). Map them to the tokens:
   - `primary` and `on-surface` `#1C1B19`; `on-primary` `#FFFFFF`; `surface` `#FFFFFF`; `surface-container*` `#FFFFFF`/`#FAFAF9`; `on-surface-variant` `#57534E`; `outline` `#D6D3CF`; `outline-variant` `#E7E5E2`; `error` = bad fg `oklch(0.48 0.15 27)`; `secondary-container` `#ECEBE8`; `on-secondary-container` `#1C1B19`. Add whatever else is needed so that no blue or azure from the M3 palette shows anywhere: focus rings, selected options, toggles, the paginator.
2. **Tokens** on `:root` as CSS custom properties prefixed `--ims-`, named after the README token list:
   `--ims-bg #F7F7F5`, `--ims-surface #FFF`, `--ims-subtle #FAFAF9`, `--ims-chip #F2F1EF`, `--ims-active #ECEBE8`, `--ims-border #E7E5E2`, `--ims-divider #F0EFED`, `--ims-input-border #D6D3CF`, `--ims-border-strong #A8A29E`, `--ims-icon-disabled #C4C0BB`, `--ims-ink #1C1B19`, `--ims-secondary #57534E`, `--ims-muted #78716C`, `--ims-faint #A8A29E`, `--ims-primary-hover #3A3835`, `--ims-mono 'IBM Plex Mono', ui-monospace, monospace`, and per tone (`neutral`, `good`, `warn`, `bad`) `--ims-<tone>-bg`, `--ims-<tone>-fg`, `--ims-<tone>-dot`. Use the hex fallbacks from the README pill table first, then override them with the oklch values inside `@supports (color: oklch(0 0 0))`. Add `--ims-good-border oklch(0.88 0.04 155)` and `--ims-bad-border oklch(0.88 0.05 25)` for the Ledger banner.
3. **Base:** `body` background `var(--ims-bg)`, colour ink, IBM Plex Sans 14/20, `-webkit-font-smoothing: antialiased`, margin 0. `*, *::before, *::after { box-sizing: border-box }`. Links: ink, underline, `text-underline-offset: 2px`, hover secondary. `.material-symbols-outlined, mat-icon { font-variation-settings: 'opsz' 20, 'wght' 350 }`.
4. **Material component overrides**, through each component's `mat.<component>-overrides((...))` mixin. Find the token names in `node_modules/@angular/material/<component>/_m3-<component>.scss` (or the `*-overrides` docs). Fall back to CSS on `.mat-mdc-*`/`.mdc-*` classes only where no token exists, and comment each such rule with why.
   - Buttons (README "Buttons"): `mat-flat-button` is primary: height 40, radius 8, 14/500, padding `0 16px`, ink background, white text, hover `#3A3835`. `mat-stroked-button` is secondary: white, 1 px `#D6D3CF` outline, ink text, hover border `#A8A29E`. Leading `mat-icon` 20 px, gap 8.
   - Form field `outline`: container height 40, radius 8, outline `#D6D3CF`, focus outline ink plus `box-shadow 0 0 0 3px rgba(28,27,25,.08)`, error outline = bad dot, error text 12 px bad fg, input text 14 px ink, placeholder faint.
   - Select: arrow `expand_more`-like, muted; panel options 14 px; selected option `#ECEBE8`.
   - Slide toggle (README "Switch"): track 34×20, radius 999, off `#D6D3CF`, on ink; knob 16 px white, shadow `0 1px 2px rgba(0,0,0,.2)`; 150 ms; no icons inside the handle; label 13–14 px secondary.
   - Paginator: 13 px secondary, transparent background; the nav buttons 32×32 with a 1 px `#E7E5E2` border and radius 8, disabled icon `#C4C0BB`.
   - Button toggle, used as the Add/Remove segment, via the `.segmented` class below.
   - Autocomplete and select panels: radius 8, 1 px border, white.
5. **Global class API.** Later prompts use these names verbatim:
   - `.page`: `display: flex; flex-direction: column; gap: 24px; min-width: 0`. `.page--narrow`: `max-width: 880px`.
   - `.page-header`: flex, `flex-wrap: wrap; align-items: flex-end; justify-content: space-between; gap: 16px`. Inside it: `.page-title` (h1 24/32 600, letter-spacing -0.01em, margin 0), `.page-subtitle` (14/20 muted, `margin: 4px 0 0`), `.page-actions` (flex, gap 8, wrap).
   - `.back-link`: inline-flex, gap 4, height 28, `padding: 0 8px 0 4px`, radius 6, 13 px secondary, no underline, hover background `#ECEBE8`; holds an 18 px `arrow_back` `mat-icon`.
   - `.section-title`: h2 16/24 600, `margin: 0 0 12px`. `.card-title`: 15/22 600, margin 0 (form-card headings).
   - `.card`: white, 1 px border, radius 12, `overflow: hidden`. `.card__body`: `padding: clamp(16px, 2.4vw, 24px)`. `.card__footer`: border-top 1 px border, `padding: 10px 16px`, flex end, wrap. Also make a `mat-paginator` inside `.card__footer` look right.
   - `.filter-bar`: padding 16, border-bottom 1 px border, flex wrap, `gap: 12px 24px`, `align-items: flex-end`.
   - `.field` (flex column, gap 6, `min-width: 0`), `.field-label` (12/16 500 secondary, `display: block`), `.field-hint` (faint, 400). `.field .mat-mdc-form-field { width: 100% }`.
   - `.data-table`: width 100%, `border-collapse: collapse`. `th`: `padding: 10px 16px`, 12/16 500 muted, text-align left, background subtle, border-bottom 1 px border, `white-space: nowrap`. `td`: `padding: 12px 16px`, 14/20, border-bottom 1 px divider. `tbody tr.clickable` cursor pointer, hover subtle. `tfoot td` background subtle, 600. `.table-scroll`: `overflow-x: auto`.
   - `.num`: `text-align: right; font-variant-numeric: tabular-nums`. `.tabular`: tabular-nums only. `.mono`: IBM Plex Mono. `.muted`, `.secondary`, `.faint`: the text colours. `.strong`: 500. `.tone-bad`: bad fg.
   - `.stacked-list` (no list styles, margin 0, padding 0) and `.stacked-row` (flex, `justify-content: space-between`, gap 12, `padding: 14px 16px`, border-bottom 1 px divider, colour ink, no underline; `a.stacked-row:hover` subtle; last row no border; `min-height: 44px`).
   - `.banner`: flex, gap 10, `padding: 12px 16px`, radius 10, 14/20, `align-items: flex-start`, with a 20 px `mat-icon`. Tones `.banner--neutral|good|warn|bad` use `--ims-<tone>-bg` and `-fg`; links inside inherit the fg colour.
   - `.skeleton`: block, height 12, radius 4, background chip, with a subtle pulse that `@media (prefers-reduced-motion: reduce)` turns off. `.skeleton-row`: `padding: 14px 16px`, border-bottom divider, flex column, gap 8.
   - `.empty-state`: `padding: 24px 16px`, muted, centred.
   - Button modifiers, for `mat-flat-button`/`mat-stroked-button`: `.btn-sm` (height 32, 13/500, `padding: 0 12px`, border `#E7E5E2`), `.btn-md` (height 36, 13/500), `.btn-form` (height 42). `.btn-icon` makes a square icon-only stroked button (min-width 0, padding 0, 32×32, radius 8, icon secondary); `.btn-icon--36` and `.btn-icon--42` change the size, and disabled icon colour `#D6D3CF`. `.btn-dashed`: height 36, 1 px **dashed** `#C4C0BB`, radius 8, transparent, hover subtle, 13/500 ink.
   - **Mobile touch targets (plan D19, R10):** the sizes above are desktop sizes. Add one `@media (max-width: 759.98px)` block that raises every control to at least 44 px: `.btn-sm`, `.btn-md`, `.btn-dashed` and `.back-link` get `min-height: 44px`; `.btn-icon` (and its `--36`/`--42` modifiers) gets `min-width: 44px; min-height: 44px`; the paginator nav buttons become 44×44. Keep `density: 0`, and never hide `.mat-mdc-button-touch-target` or set `overflow: hidden` on a button. Desktop (≥ 760 px) is unchanged.
   - `.segmented`, on a `mat-button-toggle-group`: track `#F2F1EF`, padding 3, radius 8, height 42, no outer border, no dividers. Selected toggle white with shadow `0 1px 2px rgba(0,0,0,.08)`, ink text; unselected muted.
   - AG Grid helpers (the grid DOM is outside component styles): `.ag-cell.stock-negative` → `color: var(--ims-bad-fg)` (keep this class; it exists today). `.ag-row.clickable { cursor: pointer }`. `.ag-row-pinned` subtle background and 600 weight. `.ag-cell.cell-strong` 500. `.ag-cell .mono` works, because the classes are global.
   - `.visually-hidden` (the standard clip pattern).

### `web/src/app/shared/grid-theme.ts` (new)
```ts
import { themeQuartz } from 'ag-grid-community';
/** Shared AG Grid look for Stock and Documents (handoff README "Tables"). */
export const appGridTheme = themeQuartz.withParams({ ... });
```
Params to set (check each name against `node_modules/ag-grid-community` 36.2.0 typings and drop any that don't exist): `fontFamily` IBM Plex Sans, `fontSize` 14, `headerFontSize` 12, `headerFontWeight` 500, `headerTextColor` `#78716C`, `headerBackgroundColor` `#FAFAF9`, `backgroundColor` `#FFFFFF`, `foregroundColor` `#1C1B19`, `borderColor` `#E7E5E2`, `rowBorder` `{ color: '#F0EFED' }`, `headerRowBorder` `{ color: '#E7E5E2' }`, `columnBorder` false, `wrapperBorder` false, `wrapperBorderRadius` 0, `cellHorizontalPadding` 16, `rowHoverColor` `#FAFAF9`, `selectedRowBackgroundColor` `#F2F1EF`, `accentColor` `#1C1B19`, `headerHeight` 41, `rowHeight` 48. Screens may override `rowHeight` per grid.

## Do not
- Touch files outside the `files` list. If you must, stop and report why. In particular, no component `.ts`/`.html`/`.scss` (later prompts restyle the screens) and no edit to anything under `docs/design_handoff_inventory_redesign/`.
- Add npm packages, a CSS framework, Tailwind or a font package.
- Use `::ng-deep`.
- Create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)/web"
npm run lint && npm run build && npm test -- --watch=false
grep -q "IBM+Plex+Sans" src/index.html && ! grep -q "Roboto" src/index.html src/styles.scss
grep -q "appGridTheme" src/app/shared/grid-theme.ts
for c in page page-header page-title page-subtitle page-actions back-link section-title card-title card card__body card__footer filter-bar field field-label field-hint data-table table-scroll num mono stacked-row banner--bad skeleton empty-state btn-sm btn-md btn-form btn-icon btn-dashed segmented visually-hidden; do grep -q "\.$c\b" src/styles.scss || echo "MISSING .$c"; done   # prints nothing
```
- `grep -q "max-width: 759.98px" src/styles.scss` succeeds, and at 375 px (DevTools device mode) every existing paginator button and stroked/flat button measures ≥ 44 px tall (D19).
- `npm run build` reports no budget error, and no blue or azure accent is visible on the existing screens when the app runs (buttons are ink and white, toggles ink).
