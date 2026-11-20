---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 12
unit: IMS-36
status: todo
model: sonnet
effort: medium
depends_on: [11]
parallel_group: L
files:
  create:
    - web/src/app/assistant/assistant.service.ts
    - web/src/app/assistant/assistant.service.spec.ts
    - web/src/app/assistant/assistant.ts
    - web/src/app/assistant/assistant.html
    - web/src/app/assistant/assistant.scss
    - web/src/app/assistant/assistant.spec.ts
  modify:
    - web/src/app/app.routes.ts
    - web/src/app/app.html
  delete: []
---
# 12 · Chat page

## Task
Add `/assistant`: a chat page over `GET /api/assistant/status` and `POST /api/assistant/chat`. The conversation lives in memory only, replies render as plain text, tool-use chips show what was checked, there's a "not set up" state, and errors go through `problemMessage`. Draft cards come in prompt 13.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop. IMS-35 is merged.
- API (spec §9):
  - `GET /api/assistant/status` → `{ configured: boolean, model: string | null }`.
  - `POST /api/assistant/chat` takes `{ messages: [{ role: 'user'|'assistant', content }] }` and returns `{ reply, toolCalls: [{ name, arguments, succeeded }], drafts: [...], usage: { inputTokens, outputTokens, modelCalls }, model }`.
  - Limits: 20 messages; user ≤ 2,000 chars; assistant ≤ 8,000; total ≤ 24,000.
  - Errors: 400 `validation`, 503 `assistant-not-configured`/`assistant-unavailable`, 429 `rate-limited` (all in `PROBLEM_MESSAGES`).
- Draft shape, which prompt 13 renders (put the types here): `{ type: 'receipt'|'issue'|'transfer'|'adjustment', location?, fromLocation?, toLocation?: { id, code, name } | null, reasonCode?, reference?, notes?, lines: [{ itemId, sku, itemName, baseUnit, quantity, unit, unitCost, direction }], warnings: string[] }`. Reuse `PostingType` from `web/src/app/documents/documents.service.ts:7` and `AdjustmentReason` from `shared/format.ts:6`.
- Plan decisions:
  - **D5**: in memory only. No `localStorage`/`sessionStorage`. Clear the conversation when the signed-in user's id changes (`AuthService.user` signal, `core/auth/auth.service.ts:18-20`).
  - **D20**: render replies with interpolation only (`{{ }}`), `white-space: pre-wrap`. Never `[innerHTML]`, and no markdown.
  - **D19**: no streaming. Show a progress state.
- Patterns to copy:
  - Service + spec: `web/src/app/stocktakes/stocktakes.service.ts` and its spec (`HttpClient`, `firstValueFrom`, `HttpTestingController`).
  - Page + spec: `web/src/app/stocktakes/stocktakes.ts|html|scss|spec.ts` (standalone, signals, zoneless, `host: { class: 'page …' }`, Material modules).
  - Errors: `problemMessage(err.error)` from `core/problems/problem-messages.ts`.
  - Nav: `app.html:21-115`. Copy the markup of the "Count stock" item exactly (class `nav-item`, `routerLinkActive`, `mat-icon`).
- e2e specs select links by name ("Ledger health" etc.; TASKS.md Downstream IMS-16). The new link's text is exactly `Assistant`, so it collides with none of them.

### Files
1. `assistant.service.ts` (`providedIn: 'root'`):
   - Types: `AssistantStatus`, `ToolCallSummary`, `AssistantDraft`, `AssistantDraftLine`, `AssistantReply`, and `ChatEntry { role: 'user'|'assistant'; content: string; toolCalls?: ToolCallSummary[]; drafts?: AssistantDraft[] }`.
   - Constants `MAX_MESSAGES = 20`, `MAX_USER_CHARS = 2000`, `MAX_TOTAL_CHARS = 24000`.
   - `status(): Promise<AssistantStatus>`.
   - `readonly conversation` signal (read-only view).
   - `async send(text: string): Promise<void>`:
     - Trim; reject blank or over 2,000 chars locally.
     - Append the user entry and build the history from the newest entries: role and content only, at most 20, dropping the oldest until the total ≤ 24,000.
     - POST it and append the assistant entry, with its `toolCalls` and `drafts`.
     - On error, remove the user entry and rethrow.
   - `clear()`.
   - In the constructor, an `effect` that clears the conversation when `auth.user()?.id` changes.
2. `assistant.ts|html|scss`, component `Assistant`:
   - Loads status on init.
   - When not configured: a banner reading `PROBLEM_MESSAGES['assistant-not-configured']`, with the input and Send disabled.
   - Empty state: the heading "Ask about your stock", the subtitle "It reads your records and can draft documents for you to post.", and three suggestion buttons that fill the input: "How much stock do we have at each location?", "What did we receive this week?", "Ilan pa ang bigas sa bawat branch?".
   - The message list has `aria-live="polite"`. User entries align right, assistant entries left, and content is `{{ entry.content }}` in an element with `white-space: pre-wrap`.
   - Tool chips under an assistant entry. Labels: `get_stock` "Checked stock", `find_items` "Searched items", `get_item` "Read an item", `list_locations` "Listed locations", `get_item_history` "Read item history", `list_documents` "Listed documents", `get_document` "Read a document", `get_ledger_health` "Checked ledger health", `draft_document` "Drafted a document". Any other name shows as-is. A failed call gets the warning tone.
   - Input:
     - A textarea with `maxlength=2000` and a counter (`123 / 2,000`).
     - Enter sends; Shift+Enter adds a newline.
     - Send is disabled while sending, when blank, or when not configured.
     - While sending, an indeterminate `mat-progress-bar` and "Thinking…".
   - On error: show `problemMessage(err.error)` and put the text back in the input.
   - A "Clear conversation" text button.
   - Footnote: "Answers come from your records. The assistant can't post anything: check every draft before you post it."
   - At 375 px it's one column, and the input stays usable above the on-screen keyboard (a sticky bottom bar).
3. `app.routes.ts`: `{ path: 'assistant', canActivate: [authGuard], loadComponent: () => import('./assistant/assistant').then((m) => m.Assistant) }`, before `admin/ledger`.
4. `app.html`: an `Assistant` nav link to `/assistant` after "Count stock", with a Material icon in the same style (for example `smart_toy`).
5. Specs:
   - `assistant.service.spec.ts`: status; the history is trimmed to 20 and to 24,000 chars; an error removes the user entry; the conversation clears when the user changes.
   - `assistant.spec.ts`:
     - The not-configured banner disables the input.
     - Send renders the reply. A reply of `<b>bold</b>` renders as literal text, with no `<b>` element in the DOM.
     - Chip labels, and the failed tone.
     - A 503 `assistant-unavailable` shows the mapped message and restores the input.
     - Enter vs Shift+Enter.
     - The counter.
     - Clear.

## Do not
- Add npm packages, markdown rendering, `innerHTML`, web storage or streaming.
- Touch files outside the `files` list. If you must, stop and report why.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
cd .. && dotnet build -warnaserror && dotnet test
```
- `grep -rn 'innerHTML\|localStorage\|sessionStorage' web/src/app/assistant` prints nothing.
- Manual check at 375 px (`ng serve`, devtools): the nav drawer still opens and lists Assistant, and the page has no horizontal scroll.
