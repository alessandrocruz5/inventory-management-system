# Handoff: Inventory UI Redesign (neutral, responsive)

## Overview
A visual and layout refresh of the Angular web app in `alessandrocruz5/inventory-management-system` (`web/src`). It covers every existing route: Stock, Items, Item detail, Locations, Documents, Document detail, Post stock (receipt / issue / transfer / adjustment) and Ledger health. The goals:
- Consistent, more generous margins and padding.
- A neutral warm-gray palette.
- Clear, consistent status indicators.
- Layouts that hold up on phones (375–430 px) as well as desktop.

No new features or data are introduced. Every field, label, column, status and permission rule maps to existing code. Labels and formats come from `shared/format.ts`.

## About the Design Files
The files in this bundle are **design references created in HTML**. They are prototypes that show the intended look and behaviour, not production code to copy. The task is to **recreate these designs in the existing Angular 22 + Angular Material + AG Grid app**, using its established patterns (standalone components, signals, SCSS per component, `mat.theme()`).

- `Inventory App.dc.html` is a single responsive, clickable prototype of the whole shell and every screen. It opens directly in a browser. Query params pick the starting screen: `?screen=stock|items|item|locations|documents|document|post|ledger`, `&type=receipt|issue|transfer|adjustment`, `&doc=d1..d9`, `&role=staff`, `&drawer=1`.
- `Inventory Mockups.dc.html` is a review board. It shows the prototype at 1280 px and 390 px for each screen, plus a recreation of the current Stock page for comparison.
- `support.js` is the runtime both files need to open. It is not part of the design.

## Fidelity
**High-fidelity.** Colours, type, spacing, radii and states are final. Recreate them pixel-perfectly, using Angular Material components restyled through theme tokens and component overrides, not by hand-building new controls where Material already has one.

Implementation guidance:
- **Theme:** switch the `mat.theme()` palette to a neutral one. Override the system variables (`--mat-sys-primary`, `--mat-sys-surface`, `--mat-sys-outline-variant`, etc.) to the tokens below. Set typography to IBM Plex Sans.
- **Form fields:** use `appearance="outline"` with the label above the field (see Inputs), or restyle the outline fields to match.
- **Tables:** for Stock / Documents / Items, keep AG Grid with a custom theme (`themeQuartz.withParams({...})` using the tokens below), or switch to `mat-table`. Either way, match row height, padding, header style and borders. On narrow screens, render the stacked-row list instead of the grid (see Responsive).
- **Status pills:** build one small shared `StatusPill` component (`tone: 'neutral' | 'good' | 'warn' | 'bad'`, `label`) and use it everywhere.

## Layout shell (replaces `app.html` toolbar)
- **Desktop (≥ 760 px):** two columns. The sidebar is 248 px wide, `position: sticky; top: 0; height: 100vh`, background `#FFFFFF` with a 1 px right border `#E7E5E2`. The main column is `flex: 1; min-width: 0`.
- **Sidebar contents, top to bottom:**
  - **Brand block:** padding `20px 20px 16px`. 32×32 tile, radius 8, background `#1C1B19`, with a white `inventory_2` icon (18 px). Next to it, "Inventory" (15/20, 600) and the tenant name below (12/16, `#78716C`, ellipsis).
  - **Nav:** padding `4px 12px 16px`. Groups are separated by a 20 px gap. Group label: 11 px, 500, uppercase, letter-spacing .06em, `#A8A29E`, padding `0 10px 6px`.
  - **Nav item:** height 36, padding `0 10px`, radius 8, gap 10, 20 px icon, 14 px label, 2 px gap between items.
    - Default: text `#57534E`, icon `#8A857F`.
    - Hover: background `#F2F1EF`, text `#1C1B19`.
    - Active (`routerLinkActive`): background `#ECEBE8`, text and icon `#1C1B19`, weight 500.
  - **Groups:**
    - **Inventory:** Stock (`inventory_2`), Items (`category`), Locations (`warehouse`), Documents (`receipt_long`).
    - **Post stock:** Receive stock (`move_to_inbox`), Use or sell stock (`outbox`), Move stock (`swap_horiz`), Correct stock (`tune`, Admin only).
    - **Admin** (Admin only): Ledger health (`monitor_heart`).
    - Items and Documents stay active on their detail routes.
  - **User footer:** padding `14px 16px`, top border `#E7E5E2`. 32 px round avatar (background `#ECEBE8`, initials 12/600 `#57534E`). Email (13/500, ellipsis) with the role below (12/16 `#78716C`). A 32×32 sign-out icon button (`logout`) on the right.
- **Mobile (< 760 px):**
  - **Top bar:** sticky, height 56, white, bottom border `#E7E5E2`. Contents: a 44×44 menu button (`menu`, 24 px), "Inventory" (15/18, 600) with the tenant below (12/16), and a "+ Post" button on the right (height 36, padding 0 12, 1 px `#D6D3CF` border, radius 8, 13/500). The Post button goes to Receive stock.
  - **Drawer:** the sidebar becomes `position: fixed`, 248 px wide, shadow `0 8px 32px rgba(0,0,0,.18)`, over a backdrop of `rgba(28,27,25,.32)`. Tapping the backdrop or a nav item closes it.
- **Main content area:** `max-width: 1240px; margin: 0 auto; padding: clamp(20px,3.4vw,40px) clamp(16px,3.4vw,40px) 48px`. Use a vertical flex with **24 px gap** between page sections.
- **Page header:** title on the left, primary action on the right. `flex-wrap: wrap; align-items: flex-end; justify-content: space-between; gap: 16px`.
  - H1: 24/32, 600, letter-spacing -0.01em.
  - Subtitle: 14/20 `#78716C`, with a 4 px gap below the H1.
  - On mobile the header action is hidden on Stock, because the top-bar Post button covers it.

## Screens

### 1. Stock on hand (`/stock`)
- Header: "Stock on hand". Subtitle "{n} balances across {m} locations". Primary button "+ Receive stock".
- **One card** (white, 1 px `#E7E5E2`, radius 12, overflow hidden) holds three parts:
  - **Filter bar:** padding 16, bottom border `#E7E5E2`, flex-wrap with gap `12px 24px`.
    - Left: "Location" select (`flex: 1 1 220px; max-width: 320px`), options "All locations" plus each active non-system location as `CODE · Name`.
    - Right: "Total value" (12/500 `#78716C`) above the amount (20/28, 600, tabular-nums). This replaces the AG Grid pinned bottom row as the headline number, and the table footer still repeats it.
  - **Table:** see the Tables spec. Columns:
    - Item: name 14/500 with SKU below in IBM Plex Mono 12/16 `#78716C`.
    - Location: mono 13 `#57534E`.
    - Status: pill.
    - Qty: right-aligned, 500, tabular.
    - Unit: `#78716C`.
    - Avg cost: right, `#57534E`, "—" when null.
    - Value: right.
    - Footer row "Total" + total value: background `#FAFAF9`, 600.
    - Row click opens `/items/:id`.
  - **Pagination row:** padding `10px 16px`, right-aligned, gap `8px 20px`, 13 px `#57534E`. Contents: "Rows per page 25", "1–11 of 11", and two 32×32 chevron buttons (1 px `#E7E5E2`, radius 8; disabled icon colour `#C4C0BB`).
- **Stock status** (derived from `onHand`; no new data):
  - < 0 → **Negative** (`bad`). Qty and Value text are also `bad.fg`.
  - = 0 → **Out of stock** (`neutral`).
  - > 0 → **In stock** (`good`).
- **Mobile:** stacked rows instead of the table. Each row has padding `14px 16px` and a bottom border `#F0EFED`.
  - Left: name (500), "SKU · LOC" (mono 12), value (12, tabular).
  - Right: qty (600) with the unit in `#78716C`, and the status pill below it, 6 px gap.

### 2. Items (`/items`)
- Header: "Items". Subtitle "{n} active items". "+ New item" button (Admin).
- Card filter bar:
  - Search input with a leading `search` icon. Placeholder "Search SKU or name". `flex: 1 1 240px; max-width: 360px`.
  - "Show inactive" switch: track 34×20, off `#D6D3CF`, on `#1C1B19`. White 16 px knob, transition .15s.
- Rows (flex-wrap, padding `12px 16px`, hover `#FAFAF9`):
  - SKU (mono 13 `#57534E`, 112 px fixed width), then name (500).
  - Right group: unit (32 px), status pill (Active = `good`, Inactive = `neutral`), then the Admin actions "Edit" (32 h, 1 px `#E7E5E2`) and Deactivate (32×32 icon button, `block`).
  - This replaces the select-then-act toolbar with per-row actions. If the team prefers to keep selection, put the actions in the filter bar instead.

### 3. Item detail (`/items/:id`)
- **Back link:** "← Stock" (13 px `#57534E`, 28 h, hover background `#ECEBE8`, radius 6).
- **Title row:** item name (H1) and status pill.
- **Meta line:** "SKU {mono}", "Unit {unit}", "On hand {total}". Labels `#57534E`, values `#1C1B19`, gap `8px 24px`.
- **Sections** (H2 16/24 600, 12 px gap to the card):
  - "Stock by location": table with columns Location (mono code + " · Name" in `#78716C`), Qty, Avg cost, Value.
  - "History": table with columns Posted, Document (link "Type · REF"), Location, From / to, Qty (signed "+24" / "-6"), Value (signed), Balance, Balance value, Posted by.
- Both tables scroll horizontally on narrow screens (`overflow-x: auto`, min-width 520 / 960).

### 4. Locations (`/locations`)
- Header: "Locations". Subtitle "Where stock is kept. System locations record stock entering and leaving." "+ New location" (Admin).
- Card list. Each row: padding 16, flex-wrap, gap `12px 24px`.
  - Left side:
    - Code badge: mono 13/500, padding `2px 8px`, radius 6, background `#F2F1EF`. Text `#A8A29E` when the location is inactive.
    - Name: 500, `#78716C` when inactive.
    - Pills, using the same rules as `locations.html`: System (`neutral`), Inactive (`neutral`), Negative stock allowed (`warn`, only when the toggle isn't shown).
  - Right side (Admin, non-system only): "Allow negative stock" switch (13 px `#57534E`), then "Edit" and "Deactivate" buttons (36 h, 1 px `#D6D3CF`, radius 8, 13/500).
- On mobile the actions wrap below the identity.

### 5. Documents (`/documents`)
- Header: "Documents". Subtitle "Every posting, newest first. Open one to see its lines or reverse it."
- Filter bar: grid `repeat(auto-fit, minmax(min(100%,180px), 220px))`, gap `12px 16px`. Fields: Type select (All types / Received / Used or sold / Moved / Corrected / Reversal), From date, To date.
- Table columns:
  - Posted: tabular, `#57534E`.
  - Type: 18 px icon in `#78716C` + label 500. Icons: Received `south_west`, Used or sold `north_east`, Moved `swap_horiz`, Corrected `tune`, Reversal `undo`.
  - Reference: mono 13.
  - Reason: "—" when empty.
  - Lines: right.
  - Posted by.
  - Status.
- **Document status:**
  - `reversedByDocumentId` → **Reversed** (`warn`).
  - `reversesDocumentId` → **Reverses another** (`neutral`).
  - Otherwise → **Posted** (`good`).
- **Mobile:** stacked rows, padding `14px 16px`.
  - 36×36 icon tile (background `#F2F1EF`, radius 8).
  - Line 1: type label (500) with the pill on the right.
  - Line 2: "REF · n lines" (13).
  - Line 3: "posted · by" (12 `#78716C`).

### 6. Document detail (`/documents/:id`)
- **Back link:** "← Documents".
- **Title row:** type label (H1), reference (mono 14 `#57534E`), status pill. A "Reverse" secondary button (`undo` icon) sits on the right, shown under the existing `canReverse()` rules.
- **Notice:** when reversed, a banner (padding `12px 16px`, radius 10, `warn` background and text, `info` icon) reads "This posting was reversed. Open it". A reversal gets the same banner in `neutral`: "This reversal undoes an earlier posting. Open it". The link uses `reversedByDocumentId` / `reversesDocumentId`.
- **Meta grid:** `repeat(auto-fit, minmax(min(100%,180px), 1fr))` with 1 px gaps over background `#E7E5E2`, so the cells read as a divided card (radius 12). Each cell: padding `14px 16px`, label 12/500 `#78716C`, value 14. Cells: Posted, Posted by, Reason (when present), Reference, Notes (when present).
- **"Lines" table:** columns Item (name + mono SKU), From → To (mono 13), Qty with unit, Unit cost, Total cost. Footer total.

### 7. Post stock (`/documents/new/:type`)
- Max width 880 px.
- **Titles and subtitles:**
  - receipt: "Receive stock" / "Record stock arriving from a supplier."
  - issue: "Use or sell stock" / "Record stock that was sold or used up."
  - transfer: "Move stock" / "Move stock between two of your locations."
  - adjustment: "Correct stock" / "Fix a count, or write off damaged or lost stock."
- **Card "Details":** padding `clamp(16px,2.4vw,24px)`, H2 15/22 600, grid `repeat(auto-fit, minmax(min(100%,220px), 1fr))` with gap 16.
  - Fields: Location (or From + To for transfer), Reason (adjustment), Reference.
  - Notes ("· optional" in `#A8A29E`) is a 2-row textarea spanning the full width.
- **Card "Lines":** the header shows "Lines" with the line count on the right. Each line is a flex-wrap row (padding `16px clamp(16px,2.4vw,24px)`, top border `#F0EFED`, gap `12px 16px`, `align-items: flex-end`):
  - **Item:** select, `flex: 3 1 240px`.
  - **Direction** (adjustment only): segmented Add / Remove. Track `#F2F1EF`, padding 3, radius 8, height 42. Selected segment is white with shadow `0 1px 2px rgba(0,0,0,.08)` and text `#1C1B19`; unselected text is `#78716C`.
  - **Quantity:** "· {unit}" hint in the label, right-aligned tabular input, `flex: 1 1 120px`.
  - **Unit cost:** "· ₱" hint. Shown for receipt, and for adjustment when Add.
  - **Remove line:** 42×42 icon button (`delete`). Disabled when there is only one line (icon `#D6D3CF`).
  - **Full-width hint below the line** (12/16, 16 px icon):
    - Normal: "On hand at {LOC}: {qty} {unit}" in `#78716C`.
    - When an outgoing quantity exceeds on-hand: "Only {qty} {unit} on hand at {LOC}. This will take the balance negative." in `bad.fg` with an `error` icon.
  - The duplicate-item error from `post-stock.html` uses the same hint slot in `bad.fg`.
  - **"+ Add line":** 36 h, 1 px **dashed** `#C4C0BB`, radius 8, transparent, hover `#FAFAF9`. Respects `maxLines`.
- **Sticky action bar** (`position: sticky; bottom: 0`): white card, radius 12, shadow `0 -4px 16px rgba(28,27,25,.04)`.
  - Left: "Total cost" (receipt/adjustment) or "Value at average cost", 12 px label, then the amount at 18/26 600.
  - Right: "Cancel" (secondary) and "Post" (primary), both 42 h.
  - The error banner from the current form sits above the Details card, using the `bad` tone.

### 8. Ledger health (`/admin/ledger`)
- Header: "Ledger health". Subtitle "Checked {date}". Actions: "Check again" (secondary, `refresh` icon) and "Rebuild balances" (primary).
- **Overall banner:** padding `16px 20px`, radius 12. A 36 px white circle holds the icon.
  - Healthy: `good` background, border `oklch(0.88 0.04 155)`, `check_circle` icon, "All checks passed" (16/24 600 in `good.fg`), sub-line "{n} checks across {m} balances."
  - Failing: `bad` background, border `oklch(0.88 0.05 25)`, `error` icon, "{n} check(s) failed", sub-line "Rebuild balances to recompute them from the movement history."
- **Invariant list card.** Each row: padding `14px 16px`.
  - Left: id (mono 12 `#78716C`, 24 px wide) and name (500).
  - Right: "{n} problems" (13 `#78716C`) and a Passed (`good`) / Failed (`bad`) pill.
  - Failed rows expand a nested table below: margin `0 16px 16px`, 1 px border, radius 8, cells padding `10px 12px`. Columns: SKU, Location, Movement, Check, Expected, Actual (500, `bad.fg`).
- The rebuild-result section reuses the same nested-table style.

## Components

**Buttons** (radius 8, 14/500, gap 8, 20 px leading icon):
- Primary: height 40 (42 in forms), padding `0 16px`, background `#1C1B19`, text `#FFFFFF`, hover `#3A3835`.
- Secondary: white background, 1 px `#D6D3CF` border, text `#1C1B19`, hover border `#A8A29E`.
- Small row actions: height 32–36, 13/500, 1 px `#E7E5E2` or `#D6D3CF` border.
- Icon buttons: square, same border, icon `#57534E`.

**Inputs and selects:**
- Field: height 40 (filters) or 42 (forms), padding `0 12px`, 1 px `#D6D3CF`, radius 8, white, 14 px `#1C1B19`.
- Label above the field: 12/500 `#57534E`, 6 px gap.
- Selects use `appearance: none` with an `expand_more` icon, 20 px `#78716C`, 10 px from the right.
- Focus (implement): border `#1C1B19` plus ring `0 0 0 3px rgba(28,27,25,.08)`.

**Tables:**
- Header cells: padding `10px 16px`, 12/500 `#78716C`, background `#FAFAF9`, bottom border `#E7E5E2`.
- Body cells: padding `12px 16px`, 14/20, bottom border `#F0EFED`. Clickable rows get pointer and hover `#FAFAF9`.
- Numbers are right-aligned with `font-variant-numeric: tabular-nums`.
- Wrap tables in `overflow-x: auto` with a sensible min-width.

**Status pill:** `inline-flex`, gap 6, padding `2px 8px 2px 7px`, radius 999, 12/18 500, nowrap. Leading 6 px dot. The colour comes from the tone:

| Tone | Background | Text | Dot |
| --- | --- | --- | --- |
| neutral | `#F2F1EF` | `#57534E` | `#A8A29E` |
| good | `oklch(0.965 0.022 155)` ≈ `#EDF7EF` | `oklch(0.40 0.07 155)` ≈ `#2F5A3B` | `oklch(0.62 0.12 155)` ≈ `#4C9A62` |
| warn | `oklch(0.965 0.035 85)` ≈ `#FBF3E2` | `oklch(0.44 0.08 65)` ≈ `#6E4F1F` | `oklch(0.74 0.14 75)` ≈ `#D49A2E` |
| bad | `oklch(0.96 0.022 25)` ≈ `#FBEFED` | `oklch(0.48 0.15 27)` ≈ `#A3302A` | `oklch(0.60 0.18 27)` ≈ `#D04A3C` |

Use the oklch values where supported. The hex values are approximations for fallbacks.

**Switch:** 34×20 track, radius 999. Off `#D6D3CF`, on `#1C1B19`. 16 px white knob at left 2 (off) or 16 (on), shadow `0 1px 2px rgba(0,0,0,.2)`, transition 150 ms. You can implement it with `mat-slide-toggle` restyled.

## Interactions & Behavior
- **Navigation:**
  - Sidebar and drawer links route as today.
  - Rows click through: stock → item, item list → item, documents → document, history "Document" link → document.
  - Back links return to their list.
  - Scroll to the top on route change.
- **Filters:** Location (stock), Type / From / To (documents), search and Show inactive (items). Behaviour is unchanged (each resets to page 1).
- **Post form:** add or remove lines, live on-hand hint per line, negative-stock warning, running total in the sticky bar. Validation messages are unchanged from `post-stock.ts`. Show them below fields in 12 px `bad.fg`, with the field border also `bad.dot`.
- **Permissions:**
  - Staff do not see Correct stock, Ledger health, New/Edit/Deactivate, the negative-stock switch or Reverse.
  - Staff see the "Negative stock allowed" pill instead of the switch.
- **Loading:** keep the existing loading signals. Show skeleton rows (gray `#F2F1EF` bars, 12 px high) inside the table card, not a page-level spinner.
- **Errors:** snackbars are unchanged. Inline alerts use the `bad` tone banner style (see Document detail notice).
- **Responsive:**
  - Breakpoint 760 px: sidebar ↔ top bar and drawer; tables ↔ stacked rows for Stock and Documents.
  - Items and Locations rows wrap naturally.
  - Detail tables scroll horizontally.
  - Page padding is fluid through `clamp()`.
  - Mobile touch targets are at least 42–44 px.

## State
No new state beyond the current components. Additions:
- `drawerOpen` (shell, mobile only).
- An `isNarrow` signal (via `BreakpointObserver` at `(max-width: 759.98px)`).
- A derived stock status per row.
- A post-form total (computed from the lines).

## Design Tokens

**Colours:**
- Page background: `#F7F7F5`
- Surface: `#FFFFFF`
- Subtle surface / table header: `#FAFAF9`
- Chip / tile background: `#F2F1EF`
- Active nav: `#ECEBE8`
- Border: `#E7E5E2`
- Row divider: `#F0EFED`
- Input border: `#D6D3CF`
- Strong hover border: `#A8A29E`
- Disabled icon: `#C4C0BB`
- Text: `#1C1B19` (ink), `#57534E` (secondary), `#78716C` (muted), `#A8A29E` (faint)
- Primary hover: `#3A3835`
- Status tones: see the pill table above.

**Spacing:** 2, 4, 6, 8, 10, 12, 14, 16, 20, 24, 40. Page padding `clamp(16px, 3.4vw, 40px)`. Section gap 24. Card inset 16 (24 for form cards).

**Radius:** 6 (code badge, small links), 8 (buttons, inputs, nav items), 10 (banners), 12 (cards), 999 (pills, switches).

**Shadows:**
- Drawer: `0 8px 32px rgba(0,0,0,.18)`
- Sticky bar: `0 -4px 16px rgba(28,27,25,.04)`
- Segmented / knob: `0 1px 2px rgba(0,0,0,.08–.2)`

**Type:**
- IBM Plex Sans (400/500/600). IBM Plex Mono (400/500) for SKUs, location codes and references.
- Sizes: H1 24/32 600 (-0.01em); H2 16/24 600 (form cards 15/22); body 14/20; small 13; caption and labels 12/16; group label 11 uppercase.
- Totals: 20/28 (stock) and 18/26 (post bar).
- Use `tabular-nums` for all numbers.

## Assets
- Icons are Material Symbols Outlined (already loaded in `web/src/index.html`), at `opsz 20, wght 350`. Names are listed in each screen spec above.
- Fonts are IBM Plex Sans and IBM Plex Mono from Google Fonts. They replace Roboto in `index.html` and `styles.scss`.
- No images.
- The sample data in the prototype (items, "Lumen Candle Co.", system location codes) is illustrative only.

## Screenshots
`screenshots/` contains captures of the prototype: `desktop-01…09-*.png` were taken at desktop width, and `mobile-*.png` were taken at 390 px (stock list, documents list, use-or-sell form, nav drawer). Two things in these captures are artifacts of how they were taken, not the design:
- Native selects always show their first option.
- The mobile captures have slightly larger page padding than a real phone.

Use the HTML prototype as the source of truth.

## Files
- `Inventory App.dc.html`: responsive prototype of all screens (open in a browser).
- `Inventory Mockups.dc.html`: review board at desktop and mobile widths, plus the "current" reference.
- `support.js`: runtime needed to open the two files above.
- Source mapping to the repo (what each screen replaces):
  - Shell: `app.html`, `app.scss`, `styles.scss`
  - Stock: `stock/*`
  - Items and Item detail: `items/*`
  - Locations: `locations/*`
  - Documents, Document detail and Post stock: `documents/*`
  - Ledger health: `ledger/*`
