---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 14
unit: IMS-36
status: done
model: sonnet
effort: high
depends_on: [13]
parallel_group: N
files:
  create: []
  modify:
    - web/src/app/documents/posting-form.ts
    - web/src/app/documents/posting-form.spec.ts
    - web/src/app/documents/post-stock.ts
    - web/src/app/documents/post-stock.html
    - web/src/app/documents/post-stock.spec.ts
  delete: []
---
# 14 · Post-stock prefill from a draft

## Task
When the post-stock form opens with `assistantDraft` in its navigation state, prefill the header and lines from it and show a notice. The person checks the form and posts it through the existing path: the same validation, Idempotency-Key and confirm dialog. The normal entry path is unchanged.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Read `web/src/app/documents/post-stock.ts` in full (532 lines). Key points:
  - The constructor (`:164-182`) subscribes to `paramMap` and calls `reset(type)` (`:352-381`), which clears the lines and pushes one empty line.
  - `loadLocations()` fills `locations()`.
  - `makeLine()` (`:383+`): changing `item` clears `unit` (`group.controls.unit.setValue('')`) and calls `loadDetail(id)` (units) and `refreshOnHand`. So set `unit` **after** the item and after its detail has loaded.
  - The `item` control holds an `Item` object (`items/items.service.ts:7`); `''` is the base unit.
  - `addLine()` (`:235`).
  - Read `post-stock.html` for the existing message/notice markup, and `post-stock.spec.ts` for the TestBed setup and the HTTP expectations.
- `posting-form.ts`: `PostingHeader` (`:13`), `PostingLine`, `LineDirection`, `MAX_LINES`. Pure functions go here with unit tests in `posting-form.spec.ts`.
- The draft type is `AssistantDraft` from `web/src/app/assistant/assistant.service.ts`. Use a type-only import.
- Treat navigation state as untrusted input. It comes from our own app, but it can be edited. Validate its shape, and ignore it entirely when anything is off.
- Navigation state: read it once in the constructor, from the in-app navigation only: `inject(Router).currentNavigation()?.extras.state` (Angular 22; check the installed API, it may be `getCurrentNavigation()`). **Don't** fall back to `history.state`: it survives a reload, and a reload must open an empty form.

### Steps
1. `posting-form.ts`: add `export interface DraftPrefill { header: PostingHeader; lines: { itemId: string; quantity: number; unitCost: number | null; unit: string | null; direction: LineDirection }[] }` and `export function draftToPrefill(type: PostingType, value: unknown): DraftPrefill | null`.
   - Return null unless: `value` is an object; `type` equals the route type; there are 1..`MAX_LINES` lines; each line's `itemId` is a non-empty string, `quantity` a finite number > 0, `unitCost` null or a finite number ≥ 0, `unit` null or a string, and `direction` `add`/`remove` (adjustment) or absent; and location ids are strings where the type needs them.
   - Header: `locationId` from `location.id` (receipt/issue/adjustment); `fromLocationId`/`toLocationId` (transfer); `reasonCode`; `reference`/`notes`, `''` when null.
2. `post-stock.ts`:
   - Hold the parsed prefill. Apply it once, on the first `reset(type)` for the matching type, after `loadLocations()` has resolved.
   - Header: set a location id only if it's in `locations()`.
   - Lines: for each line, use the first line or `addLine()`. Fetch `ItemsService.get(itemId)` and set the `item` control to that item. Once `loadDetail` has loaded its units, set `unit` (the code, or `''` when null; skip if the unit isn't active), then `quantity`, `unitCost` and `direction`.
   - A line whose item fetch fails stays empty, with the line's server-style error "This item is no longer available."
   - Set `prefilled = signal(true)`. `reset()` for another type clears it.
3. `post-stock.html`: when `prefilled()`, show an info notice at the top of the form: "Filled in by the assistant. Check each line before you post." Use the existing notice style.
4. Specs:
   - `posting-form.spec.ts`: `draftToPrefill` maps a receipt (unit and unit cost), an issue, a transfer and an adjustment (direction, reasonCode). A mismatched type, zero lines, 101 lines, a negative quantity, a missing itemId and a non-object all return null.
   - `post-stock.spec.ts`:
     - A receipt draft in state fills the location, item, unit, quantity and unit cost, and shows the notice.
     - A draft whose type doesn't match the route is ignored: one empty line, no notice.
     - A malformed draft is ignored.
     - An unknown location id is left blank.
     - Entering with no state behaves exactly as before. The existing tests pass unchanged.
     - Submitting a prefilled form posts the body `buildPostingBody` builds, with an Idempotency-Key header, as the existing submit test checks.

## Do not
- Post automatically, skip the confirm dialog, or change `submit()`, `buildPostingBody`, the idempotency key or the guard.
- Touch files outside the `files` list. If you must, stop and report why.
- Add packages. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
cd .. && dotnet build -warnaserror && dotnet test
```
- Manual check with `aspire run` and Ollama: ask "Receive 2 sacks of brown sugar at MKT at 3,650 per sack" → the draft card → Review and post → the form is prefilled with the notice → Post → the document page. A reload of the form route shows an empty form.
