---
type: prompt
plan: "[[2026-11-20-ai-assistant]]"
id: 10
unit: IMS-35
status: todo
model: sonnet
effort: high
depends_on: [09]
parallel_group: J
files:
  create:
    - src/Modules/Assistant/Assistant/Drafts/DocumentDraft.cs
    - src/Modules/Assistant/Assistant/Drafts/DraftValidator.cs
    - src/Modules/Assistant/Assistant/Tools/DraftTool.cs
    - tests/Inventory.IntegrationTests/AssistantDraftTests.cs
  modify:
    - src/Modules/Assistant/Assistant/Tools/ToolCatalog.cs
    - src/Modules/Assistant/Assistant/Prompting/SystemPrompt.cs
    - src/Modules/Assistant/Assistant/AssistantModuleExtensions.cs
    - CLAUDE.md
  delete: []
---
# 10 · Draft validation and the draft tool

## Task
Add `draft_document`: the model proposes a receipt, issue, transfer or (Admin) adjustment. The server validates it against master data and the posting limits, and collects valid drafts for the reply (prompt 11). **Nothing is posted.** A person posts through the existing form (plan D8).

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check that `git branch --show-current` equals this prompt's `unit` key. If it doesn't, stop. IMS-34 is merged.
- Read first:
  - `Tools/{InventoryTools,ToolCatalog,ToolResults}.cs` and `Prompting/SystemPrompt.cs` in `src/Modules/Assistant/Assistant`.
  - `Inventory.Contracts/IInventoryReader.cs`: `GetItemBySkuAsync` (units, active), `ListLocationsAsync` (`IsPhysical`, `IsActive`), `GetStockAsync`.
- The rules to mirror (spec §6, §9):
  - `src/Modules/Inventory/Inventory/Features/Posting/PostingValidation.cs:42-55`: `MaxAmount = 999_999_999.9999m`, `MaxLines = 100`, reference ≤ 100 and notes ≤ 500 (private consts).
  - `web/src/app/documents/posting-form.ts:55-79`: which types take a unit cost (receipt always and required; adjustment only on "add"; never issue or transfer), outflows, at most 4 dp, and quantity > 0.
  - Adjustment reasons are `Damaged, Lost, Found, CountCorrection, Other` (`Persistence/AdjustmentReason.cs`). Only Admin posts adjustments (spec §9; `web/src/app/documents/post-stock.guard.ts`).
- The Angular form takes the type in lower case (`receipt|issue|transfer|adjustment`; `documents.service.ts:7`), so drafts use those strings.
- Plan D11 (an adjustment needs Admin; the model never decides permissions) and D12 (at most 3 drafts per reply).

### Files
1. `Drafts/DocumentDraft.cs`:
   - `internal static class DraftLimits`: `MaxAmount`, `MaxLines`, `MaxReferenceLength = 100`, `MaxNotesLength = 500`, `MaxDecimals = 4`, `MaxDraftsPerReply = 3`. XML doc: "Mirror PostingValidation; AssistantDraftTests pins them (CLAUDE.md Hidden coupling)."
   - `internal sealed record DocumentDraft(string Type, DraftLocation? Location, DraftLocation? FromLocation, DraftLocation? ToLocation, string? ReasonCode, string? Reference, string? Notes, IReadOnlyList<DraftLine> Lines, IReadOnlyList<string> Warnings)`.
   - `DraftLocation(Guid Id, string Code, string Name)`.
   - `DraftLine(Guid ItemId, string Sku, string ItemName, string BaseUnit, decimal Quantity, string? Unit, decimal? UnitCost, string? Direction)`. `Unit` is null for the base unit. `Direction` is `add`/`remove` for adjustments, else null.
   - `internal sealed class DraftCollector` (scoped): `IReadOnlyList<DocumentDraft> Drafts` and `bool TryAdd(DocumentDraft)`, which is false at the cap.
2. `Drafts/DraftValidator.cs`:
   - `internal sealed class DraftValidator(IInventoryReader reader)` with `Task<DraftOutcome> ValidateAsync(DraftRequest request, string role, CancellationToken ct)`.
   - `DraftRequest(string? Type, string? Location, string? FromLocation, string? ToLocation, string? ReasonCode, string? Reference, string? Notes, IReadOnlyList<DraftLineRequest>? Lines)`; `DraftLineRequest(string? Sku, decimal? Quantity, string? Unit, decimal? UnitCost, string? Direction)`.
   - `DraftOutcome(DocumentDraft? Draft, IReadOnlyList<string> Errors)`. Errors read like `"lines[1].quantity: Must be more than 0."`, so the model can fix them.
   - Rules:
     - `type`: one of the four (case-insensitive, stored lower case). `adjustment` with a role other than `RoleNames.Admin` → `"type: Only an Admin can correct stock."`.
     - Locations, matched by code case-insensitively among `IsPhysical && IsActive`:
       - receipt, issue and adjustment need `location`, and `fromLocation`/`toLocation` must be absent;
       - transfer needs both `fromLocation` and `toLocation`, different, and `location` absent.
       - An unknown, virtual or inactive location → `"location: No active location with code X."`.
     - `reasonCode`: required for adjustment and must be one of the five (exact names, case-insensitive input, stored canonical); absent otherwise.
     - `reference` ≤ 100 chars, `notes` ≤ 500.
     - `lines`: 1..`MaxLines`, and no SKU twice.
     - Each line:
       - The SKU is active in the tenant (`GetItemBySkuAsync`, `IsActive`).
       - `quantity` > 0, ≤ `MaxAmount`, ≤ 4 dp (`decimal.Scale` after `decimal.Round(q, 4) == q`).
       - `unit` is null, the base unit (stored as null) or an active unit code of the item.
       - `unitCost`: required on a receipt; allowed only on an adjustment "add"; otherwise must be absent; ≥ 0, ≤ `MaxAmount`, ≤ 4 dp.
       - `direction`: required `add|remove` on an adjustment, absent otherwise.
     - Warnings, not errors: for outflows (issue, transfer, adjustment remove), when quantity × unit factor > on-hand at the source location (`GetStockAsync(itemId, locationId, 1)`) → `"lines[i]: Only N on hand at CODE."`.
3. `Tools/DraftTool.cs`:
   - `internal sealed class DraftTool(DraftValidator validator, DraftCollector collector)` with `Task<object> DraftAsync(string role, string type, string? location, string? fromLocation, string? toLocation, string? reasonCode, string? reference, string? notes, DraftLineRequest[] lines, CancellationToken ct)`.
   - When valid and `collector.TryAdd`: return `new { ok = true, draft = collector.Drafts.Count, summary = "<Type> at <codes>, <n> line(s). Not posted: the user must open it with Review and post.", warnings }`.
   - When the cap is reached: `{ ok = false, errors = ["At most 3 drafts per reply."] }`. When invalid: `{ ok = false, errors }`.
4. `Tools/ToolCatalog.cs`:
   - Add `draft_document` to both roles, as a lambda that forwards to `DraftTool.DraftAsync(role, …)`, with `[Description]` on the lambda parameters (C# allows attributes on lambda parameters).
   - Description: "Prepare a draft receipt, issue, transfer, or adjustment (Admin only) for the user to review and post. It does not post. Quantities and unit costs are per the given unit (default: the item's base unit)."
   - Line fields described: `sku`, `quantity`, `unit` (optional), `unitCost` (receipt; adjustment add), `direction` (adjustment: add/remove).
5. `Prompting/SystemPrompt.cs`: after the "You can only read…" line, add: `- To record stock, call draft_document. A draft is only a proposal: tell the user to open it with Review and post, check it and post it. Never say it was posted.`
6. `AssistantModuleExtensions.cs`: register `DraftValidator`, `DraftTool` and `DraftCollector` as scoped. `ToolCatalog` gets `DraftTool` through its constructor.
7. `CLAUDE.md` `## Hidden coupling`: add the bullet `` - `src/Modules/Assistant/Assistant/Drafts/DocumentDraft.cs` `DraftLimits` mirror `PostingValidation` (max amount, lines, reference and notes lengths) and the post-stock form's line rules; `AssistantDraftTests.Draft_limits_mirror_posting_validation` pins them. ``
8. `tests/Inventory.IntegrationTests/AssistantDraftTests.cs`:
   - Invoke through the catalog in a tenant scope, as `AssistantToolTests` does, and read `DraftCollector` from the same scope.
   - Tests:
     1. `A_receipt_in_sacks_becomes_a_draft`: an item with unit `sack` = 25 (POST `/api/items/{id}/units`); `type` "Receipt", `location` lower case, `sku`, qty 2, unit `SACK`, unitCost 1250 → ok. The draft has ids, codes and names, `unit: "sack"`, `unitCost: 1250`. `GET /api/documents` `total` is unchanged.
     2. `Only_an_admin_drafts_an_adjustment`: Staff → error; Admin with `Damaged`/`remove` → ok.
     3. `Foreign_inactive_and_virtual_references_are_rejected`: B's SKU, B's location code, an inactive item, `SUP`, an inactive location.
     4. `[Theory] Line_rules_match_posting`: one error key per row for qty 0, qty −1, qty 1.00001, qty > MaxAmount, a receipt without unitCost, an issue with unitCost, an unknown unit, a duplicate SKU, 101 lines, transfer from == to, an adjustment without reasonCode, an adjustment with direction `up`, a 101-char reference, 501-char notes, and type `sale`.
     5. `Outflows_over_on_hand_warn_but_still_draft`.
     6. `At_most_three_drafts_per_reply`.
     7. `Draft_limits_mirror_posting_validation`: `DraftLimits.MaxAmount == PostingValidation.MaxAmount` and `MaxLines == PostingValidation.MaxLines`. Read the private `MaxReferenceLength`/`MaxNotesLength` with `typeof(PostingValidation).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()`.

## Do not
- Call any posting handler or route, or reference `IInventoryModule` (T12 forbids it).
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*AssistantDraftTests' --filter-class '*AssistantToolTests'
dotnet test --project tests/Architecture.Tests
dotnet test
```
