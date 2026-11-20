---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 13
unit: IMS-36
status: todo
model: sonnet
effort: medium
depends_on: [12]
parallel_group: M
files:
  create:
    - web/src/app/assistant/draft-card.ts
    - web/src/app/assistant/draft-card.spec.ts
  modify:
    - web/src/app/assistant/assistant.ts
    - web/src/app/assistant/assistant.html
  delete: []
---
# 13 · Draft cards

## Task
Render each draft in an assistant reply as a card that summarises it, with a **Review and post** button that opens the post-stock form carrying the draft in navigation state. Prompt 14 makes the form read it.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Read:
  - `web/src/app/assistant/assistant.service.ts` (the `AssistantDraft` types) and `assistant.ts|html`.
  - `web/src/app/documents/posting-form.ts`: `POSTING_TITLES` (`:35`).
  - `web/src/app/shared/format.ts`: `formatQuantity`, `formatMoney`, `reasonLabel`.
  - `web/src/app/shared/status-pill.ts` for a small standalone component with an inline template.
  - `web/src/app/core/auth/auth.service.ts`: `isAdmin`.
- Route: `/documents/new/:type` (`app.routes.ts`), where `type` is `receipt|issue|transfer|adjustment`. Staff can't open `adjustment` (`post-stock.guard.ts`). The server never offers Staff an adjustment draft (plan D11), and the card also hides the button for non-Admins.
- Plain text only (plan D20): interpolation, no `innerHTML`.

### Steps
1. `draft-card.ts`: standalone `DraftCard`, selector `app-draft-card`, with `draft = input.required<AssistantDraft>()`. Inline template and styles in the app's card style. It shows:
   - The title `POSTING_TITLES[type]` with a "Draft" pill.
   - Where: "At QC · Quezon City branch", or for a transfer "From QC to MKT".
   - The reason (`reasonLabel`) and the reference, when present.
   - The lines: `SKU · name`, `formatQuantity(quantity)` plus the unit (the unit, or `baseUnit` when null), `@ formatMoney(unitCost)` when present, and "Add"/"Remove" for adjustments.
   - Warnings, as a list in the warning tone.
   - A **Review and post** button: `router.navigate(['/documents/new', draft.type], { state: { assistantDraft: draft } })`. Hidden when `type === 'adjustment' && !auth.isAdmin()`.
   - The note "Not posted yet."
2. `assistant.ts|html`: import `DraftCard`, and render `@for (d of entry.drafts ?? []; track $index) { <app-draft-card [draft]="d" /> }` under the assistant entry's text.
3. `draft-card.spec.ts`:
   - Receipt in a unit.
   - A transfer's from/to.
   - An adjustment's reason and direction.
   - Warnings show.
   - The button navigates with the state (spy on `Router.navigate`).
   - The button is hidden for Staff on an adjustment.
   - An item name with `<script>` renders as text.

## Do not
- Post anything from the card, or call the API.
- Touch files outside the `files` list. If you must, stop and report why.
- Add packages. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
```
