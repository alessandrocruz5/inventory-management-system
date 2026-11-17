---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 10
unit: IMS-25
status: todo
model: sonnet
effort: medium
depends_on: [09]
parallel_group: B
files:
  create: []
  modify:
    - web/src/app/ledger/ledger-health.ts
    - web/src/app/ledger/ledger-health.html
    - web/src/app/ledger/ledger-health.scss
    - web/src/app/ledger/ledger-health.spec.ts
  delete: []
---
# 10 · Ledger health screen

## Task
Restyle `/admin/ledger` to the handoff:
- A header with "Checked {date}" and the icon actions "Check again" and "Rebuild balances".
- An overall healthy or failing banner.
- An invariant list card with Passed/Failed pills, where failed rows expand a nested failure table.
- The rebuild result in the same nested-table style.

The data flow, the confirm dialog, the busy guards and the error handling are unchanged.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`.
- IMS-22 globals in `web/src/styles.scss` (don't edit it): `.page`, `.page-header`, `.page-title`, `.page-subtitle`, `.page-actions`, `.card`, `.data-table`, `.table-scroll`, `.num`, `.mono`, `.muted`, `.strong`, `.tone-bad`, `.banner`, `.banner--bad`, `.skeleton*`. Tokens `--ims-good-bg|fg`, `--ims-bad-bg|fg`, `--ims-good-border`, `--ims-bad-border`. `StatusPill` is in `shared/status-pill.ts`. The `mat-icon` default font is Material Symbols. The shell pads the main area.
- **Spec:** `docs/design_handoff_inventory_redesign/README.md` §8 "Ledger health". Screen: `screenshots/desktop-09-ledger-health.png`. Prototype: `Inventory App.dc.html?screen=ledger`.
- **Plan D13:**
  - Keep the ids `INV-1`…`INV-5` and the existing `INVARIANT_LABELS` (`ledger-health.ts:16-22`); the prototype's `L1` names are sample data.
  - The healthy sub-line is `Ran {n} checks.` (n = `invariants.length`), because the health response has no balance count.
  - The failing sub-line is "Rebuild balances to recompute them from the movement history."

### Hard contracts (e2e `tests/e2e/specs/ledger-health.spec.ts`)
- The visible text "All checks passed" stays exactly.
- `getByText('Passed', { exact: true })` must count **5** on a healthy ledger. Render each invariant's status only as `<app-status-pill tone="good" label="Passed">`, whose label is the host's only text node. No other element may have the exact text "Passed".
- The buttons "Rebuild balances" and "Check again" keep those names. The confirm dialog button stays "Rebuild".
- `Checked {n} balances. Nothing to fix.` stays exactly, as e2e matches `/Nothing to fix\./`.

### Layout
- `host: { class: 'page' }`.
- **`.page-header`:**
  - `<h1 class="page-title">Ledger health</h1>`, with the subtitle `Checked {{ formatDate(h.checkedAt) }}` when `health()` is set.
  - `.page-actions`:
    - `<button mat-stroked-button type="button" …><mat-icon>refresh</mat-icon>Check again</button>`.
    - `<button mat-flat-button type="button" …>Rebuild balances</button>`.
    - Both keep their `[disabled]` and `(click)` bindings.
- **Error:** `<p class="banner banner--bad" role="alert"><mat-icon>error</mat-icon>{{ message }}</p>`.
- **Overall banner** (`.overall`): `padding: 16px 20px`, radius 12, flex, gap 14, `align-items: center`.
  - A 36 px white circle holds a 22 px icon.
  - Healthy (`.overall--good`): background `--ims-good-bg`, border 1 px `--ims-good-border`, icon `check_circle` in good fg. Title "All checks passed" (16/24 600, good fg), with the sub-line `Ran {n} checks.` (13 px secondary).
  - Failing (`.overall--bad`): bad bg and border, icon `error`. Title `1 check failed` or `{n} checks failed`, in bad fg; sub-line as in D13.
- **Rebuild result,** when set: a `.card`.
  - With no differences: `<p class="card-line">Checked {{ r.balancesChecked }} balances. Nothing to fix.</p>` (`padding: 14px 16px`).
  - Otherwise: a 14px/16px-padded heading `Fixed {n} balances`, then the nested table below with the same columns and before→after cells as today, plus the `(row was missing)` note (muted).
- **Invariant list:** one `.card`, `@for` each invariant, `<section class="invariant" [attr.data-passed]="inv.passed">`:
  - The row (`padding: 14px 16px`, flex, gap 12, `align-items: center`, border-bottom divider): the id (`.mono`, 12 px muted, `min-width: 44px`), the name (500, `label(inv.id, inv.name)`), a spacer, then, when failed, `{{ inv.failureCount }} problems` (13 px muted) with the existing `, showing the first 100` suffix when truncated, then the pill: Passed (`good`) or Failed (`bad`).
  - When failed, the nested table below it, in `.nested` (`margin: 0 16px 16px; border: 1px solid var(--ims-border); border-radius: 8px; overflow-x: auto`):
    - `<table class="data-table">` with cells padded `10px 12px` (override it inside `.nested` only).
    - Columns: SKU (`.mono`), Location (`.mono`), Movement, Check, Expected (`.num`), Actual (`.num strong tone-bad`).
    - The existing `amount()` and "—" formatting stays.
- **Loading:** while `loading()` and there's no `health()`, a `.card` with 5 `.skeleton-row`s instead of "Checking the ledger…".
- `ledger-health.ts`: add `MatIconModule` and `StatusPill` to `imports`. Add a `failedTitle(h)` helper returning "1 check failed" or "{n} checks failed". No other logic changes.
- `ledger-health.scss`: rewrite to these rules only, under 3 kB.

### `ledger-health.spec.ts`
Keep every case's intent:
- `.invariant` stays, with count 5.
- `.marker.pass` becomes `.invariant[data-passed="true"] app-status-pill` (5, text "Passed").
- `.marker.fail` becomes `app-status-pill[data-tone="bad"]`, text "Failed".
- `table` absent when healthy, `tbody tr` rows when failing, `.rebuild tbody tr`: give the rebuild card `class="card rebuild"` so that selector keeps working.
- `[role="alert"]` for errors.
- The button lookup at `:83` matches on `textContent.includes(...)`, because the icon ligature text is in `textContent`.

Add:
- A healthy render shows `Ran 5 checks.`.
- One failure shows "1 check failed".

## Do not
- Touch files outside the `files` list. If you must, stop and report why. Not `ledger.service.ts` or `styles.scss`.
- Change `check()`, `confirmRebuild()`, `load()`, the dialog text or the invariant labels.
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
- `/admin/ledger` at 1280 px matches `desktop-09-ledger-health.png`. Nested tables scroll inside the card at 375 px.
- CI `e2e` passes (`ledger-health.spec.ts`: exactly 5 "Passed", "All checks passed", "Nothing to fix.", the Staff link count 0; `t14-stock-flow.spec.ts` with prompt 09's text).
