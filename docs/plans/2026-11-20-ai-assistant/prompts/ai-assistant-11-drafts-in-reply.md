---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 11
unit: IMS-35
status: done
model: sonnet
effort: medium
depends_on: [10]
parallel_group: K
files:
  create: []
  modify:
    - src/Modules/Assistant/Assistant/Features/Chat/Handler.cs
    - src/Modules/Assistant/Assistant/Features/Chat/Response.cs
    - tests/Inventory.IntegrationTests/AssistantTests.cs
    - "docs/Inventory System — Sprint 1 Spec.md"
  delete: []
---
# 11 · Drafts in the chat reply

## Task
Return the turn's validated drafts in `POST /api/assistant/chat`, prove end to end that a drafting turn posts nothing, and document the field in spec §9.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Read: `src/Modules/Assistant/Assistant/Features/Chat/{Handler,Response}.cs`, `Drafts/DocumentDraft.cs` (`DraftCollector`, `DocumentDraft`), `tests/Inventory.IntegrationTests/AssistantTests.cs` and `ScriptedChatClient.cs`.
- `DraftCollector` is scoped, so the handler and the `draft_document` tool share one per request.

### Steps
1. `Response.cs`: add `IReadOnlyList<DocumentDraft> Drafts` to `AssistantChatResponse`, after `ToolCalls`.
2. `Handler.cs`: inject `DraftCollector` and return `collector.Drafts` in the response. A failed turn (4xx/5xx) returns no drafts.
3. `AssistantTests.cs`:
   - `Chat_returns_drafts_and_posts_nothing`: the script calls `draft_document` (a receipt of 2 sacks at the user's location), then replies. Assert `drafts[0].type == "receipt"`, its location code, a line with `unit: "sack"` and `quantity: 2`, and `toolCalls[0].succeeded`. `GET /api/documents` `total` and `GET /api/stock` are unchanged.
   - `A_refused_draft_is_not_returned`: Staff, the script drafts an adjustment → `drafts` is empty and `toolCalls[0].succeeded == false`.
4. Spec §9: on the `POST /assistant/chat` row, change the return value to `{ reply, toolCalls, drafts, usage, model }`. In the Assistant bodies paragraph, add: "`drafts` holds up to 3 validated document drafts (receipt, issue, transfer; adjustment for Admin) with ids, codes and names. The user posts a draft through the post-stock form; the assistant never posts."

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*AssistantTests' --filter-class '*AssistantDraftTests' --filter-class '*AssistantSecurityTests'
dotnet test
```
