---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 03
unit: IMS-34
status: done
model: haiku
effort: low
depends_on: []
parallel_group: C
files:
  create: []
  modify:
    - src/Shared/Kernel/Problems/ProblemTypes.cs
    - web/src/app/core/problems/problem-messages.ts
    - web/src/app/core/problems/problem-messages.spec.ts
    - "docs/Inventory System — Sprint 1 Spec.md"
  delete: []
---
# 03 · Assistant problem slugs and spec

## Task
Add three ProblemDetails slugs in all three places the hidden coupling requires, and record the Assistant module, its routes and T19 in the spec.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop.
- Hidden coupling (CLAUDE.md; TASKS.md Downstream IMS-3): each new slug lands in spec §9, Kernel `ProblemTypes` and Angular `PROBLEM_MESSAGES` **in the same PR**.
- Slugs (plan D13):

| Constant | Slug | Status | Extra | Angular message |
|---|---|---|---|---|
| `AssistantNotConfigured` | `assistant-not-configured` | 503 | — | `The assistant isn't set up on this server.` |
| `AssistantUnavailable` | `assistant-unavailable` | 503 | `Retry-After` header when known | `The assistant's model didn't respond. Try again in a moment.` |
| `RateLimited` | `rate-limited` | 429 | `Retry-After` header | `You're sending messages too quickly. Wait a minute, then try again.` |

- `src/Shared/Kernel/Problems/ProblemTypes.cs`: add three `public const string`s after `StocktakeNotOpen`, each with an XML summary like the others ("The assistant has no model endpoint configured (plan D14).", "The model timed out, failed or was rate-limited upstream (plan D13).", "The caller sent too many requests in the window (plan D12).").
- `web/src/app/core/problems/problem-messages.ts`: add the three entries at the end of `PROBLEM_MESSAGES`.
- `problem-messages.spec.ts`: add `it('maps the assistant slugs', ...)` covering the three, in the style of `'maps the stocktake slugs'`.
- Spec `docs/Inventory System — Sprint 1 Spec.md`:
  - §4 (around lines 156–200): where it lists the modules, add one line: Assistant is a module under `src/Modules/Assistant/Assistant` with no Contracts project. It reads Inventory only through `Inventory.Contracts.IInventoryReader` and never writes; T12 enforces both.
  - §9 route table (after `GET /stock`, around line 600):
    - `| \`GET /assistant/status\` | any | Whether a model is configured, and its name |`
    - `| \`POST /assistant/chat\` | any | One assistant turn over read-only tools; body \`{ messages: [{ role, content }] }\`; returns \`{ reply, toolCalls, usage, model }\` |`
  - §9: add a short **Assistant bodies** paragraph after the stocktake bodies paragraph:
    - Roles are `user` and `assistant`, and the last one is `user`.
    - At most 20 messages; a user message is 1–2,000 chars, an assistant message ≤ 8,000, and the total ≤ 24,000 (otherwise `400 validation` with `messages[i].role` or `messages[i].content`, or `messages`).
    - At most 5 tool rounds and a 90 s budget per turn.
    - A reply is at most 8,000 chars, so it always fits back into the history. A longer one is cut and ends with `[Reply cut off: too long.]`.
    - 10 turns a minute per user.
    - The server stores no conversation.
  - §9 problem-type table: add the three rows (slug, status, extra fields as above).
  - §11 test table: add `| T19 | Assistant: tools in the caller's tenant, role-gated tools, no identity in tool results, history and size limits, tool-round cap, reply cap, timeout, rate limit, no writes | Integration | Plan [[2026-11-20-ai-assistant]] R3 |`.
  - §15 Log: `- 2026-11-20: Sprint 4 ([[2026-11-20-ai-assistant]]) adds the Assistant module (§4), its routes and slugs (§9) and T19 (§11).`

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Rename or reorder existing slugs or messages.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
cd web && npm run lint && npm run build && npm test -- --watch=false && cd ..
grep -c 'assistant-not-configured\|assistant-unavailable\|rate-limited' "docs/Inventory System — Sprint 1 Spec.md" src/Shared/Kernel/Problems/ProblemTypes.cs web/src/app/core/problems/problem-messages.ts
```
- Each of the three files reports 3 or more matches.
