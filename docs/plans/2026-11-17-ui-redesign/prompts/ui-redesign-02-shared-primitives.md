---
type: prompt
plan: "[[2026-11-17-ui-redesign]]"
id: 02
unit: IMS-22
status: done
model: sonnet
effort: medium
depends_on: [01]
parallel_group: B
files:
  create:
    - web/src/app/shared/status-pill.ts
    - web/src/app/shared/status-pill.spec.ts
    - web/src/app/shared/status-pill-cell.ts
    - web/src/app/shared/layout.ts
  modify:
    - web/src/app/shared/format.ts
    - web/src/app/shared/format.spec.ts
  delete: []
---
# 02 · Status helpers, StatusPill, its AG Grid cell, and `injectIsNarrow`

## Task
Add the shared building blocks the screens use:
1. Pure status functions in `shared/format.ts`.
2. A `StatusPill` component.
3. An AG Grid cell renderer that wraps it.
4. An `injectIsNarrow()` signal helper.

Test the first two.

## Context
- Repo root: `git rev-parse --show-toplevel`; stay on the current branch (the unit's key) and stop if it is `develop` or `main`. Prompt 01 already added the `--ims-*` tokens (including `--ims-<tone>-bg|fg|dot` for `neutral|good|warn|bad`) to `web/src/styles.scss`.
- Spec: `docs/design_handoff_inventory_redesign/README.md`, "Status pill", Stock "Stock status" and Documents "Document status".
- Conventions: standalone components, signals (`input()`, `input.required()`, `computed`), `@if`/`@for`, zoneless. Tests are Vitest through `ng test` (`describe`/`it`/`expect`, `vi.fn()`). Copy the style of `web/src/app/shared/format.spec.ts` and of a component spec such as `web/src/app/items/items.spec.ts:1-40` (TestBed, `fixture.whenStable()`).
- `shared/format.ts` is imported by 24 files, so make **additive changes only** (plan D12) and don't touch existing exports.

### 1. `web/src/app/shared/format.ts`: append
```ts
export type StatusTone = 'neutral' | 'good' | 'warn' | 'bad';
export interface Status { tone: StatusTone; label: string; }

/** Stock status from on-hand quantity (handoff README "Stock status"). */
export function stockStatus(onHand: number): Status
//  < 0 → { tone: 'bad', label: 'Negative' }; 0 → { tone: 'neutral', label: 'Out of stock' }; > 0 → { tone: 'good', label: 'In stock' }

/** Document status (handoff README "Document status"); reversedBy wins. */
export function documentStatus(doc: { reversesDocumentId: string | null; reversedByDocumentId: string | null }): Status
//  reversedByDocumentId → { tone: 'warn', label: 'Reversed' }; reversesDocumentId → { tone: 'neutral', label: 'Reverses another' }; else { tone: 'good', label: 'Posted' }
```
Add `describe` blocks to `format.spec.ts` covering every branch, including `-0.0001`, `0` and `0.0001`.

### 2. `web/src/app/shared/status-pill.ts` (new)
- `selector: 'app-status-pill'`, standalone, with `tone = input<StatusTone>('neutral')` and `label = input.required<string>()`.
- Host bindings: `class: 'pill'` and `[class]` / `[attr.data-tone]` set to the tone, so `pill--good` etc. apply.
- Template: `<span class="dot" aria-hidden="true"></span>{{ label() }}`. The label **must be a direct text node of the host element, and the only text in it**: the e2e suite counts `getByText('Passed', { exact: true })`, so wrapping the label in another element or adding more text would change what matches.
- Inline `styles` (keep it small, well under 1 kB): `:host { display: inline-flex; align-items: center; gap: 6px; padding: 2px 8px 2px 7px; border-radius: 999px; font: 500 12px/18px inherit; white-space: nowrap; background: var(--ims-neutral-bg); color: var(--ims-neutral-fg) }`, with `.dot { width: 6px; height: 6px; border-radius: 50%; background: var(--ims-neutral-dot) }` and one host-class rule per tone switching the three variables.
- `status-pill.spec.ts`: it renders the label, applies the tone (`data-tone`), defaults to `neutral`, and its host `textContent.trim()` equals the label exactly.

### 3. `web/src/app/shared/status-pill-cell.ts` (new)
An AG Grid Angular **component** renderer (plan D11: never an HTML-string renderer):
```ts
@Component({ selector: 'app-status-pill-cell', imports: [StatusPill],
  template: `@if (status(); as s) { <app-status-pill [tone]="s.tone" [label]="s.label" /> }` })
export class StatusPillCell implements ICellRendererAngularComp {
  protected readonly status = signal<Status | null>(null);
  agInit(params: ICellRendererParams<unknown, Status | null | undefined>): void { this.status.set(params.value ?? null); }
  refresh(params: ICellRendererParams<unknown, Status | null | undefined>): boolean { this.status.set(params.value ?? null); return true; }
}
```
The column's `valueGetter` supplies the `Status`, and pinned rows supply `undefined`, which renders nothing. Import `ICellRendererAngularComp` from `ag-grid-angular` and `ICellRendererParams` from `ag-grid-community`; check the exact names in `node_modules/ag-grid-angular` 36.2.0.

### 4. `web/src/app/shared/layout.ts` (new)
```ts
export const NARROW_QUERY = '(max-width: 759.98px)';
/** True below the 760 px breakpoint (handoff README "Responsive"). Call in an injection context. */
export function injectIsNarrow(): Signal<boolean>
```
Build it from `inject(BreakpointObserver)` (`@angular/cdk/layout`, already a dependency): `toSignal(observer.observe(NARROW_QUERY).pipe(map((r) => r.matches)), { initialValue: observer.isMatched(NARROW_QUERY) })`. Under jsdom, `window.matchMedia` is missing, the CDK falls back to no match, and this returns `false`. Tests that need the mobile branch provide `{ provide: BreakpointObserver, useValue: { observe: () => of({ matches: true, breakpoints: {} }), isMatched: () => true } }`. Write that line as a JSDoc example on the function.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Change or remove any existing export of `format.ts`.
- Add dependencies or abstractions beyond these four pieces.
- Create or switch branches, commit, or push.

## Done when
```bash
cd "$(git rev-parse --show-toplevel)/web"
npm run lint && npm run build && npm test -- --watch=false
```
- The new `format.spec.ts` and `status-pill.spec.ts` cases pass, and no existing spec changed.
