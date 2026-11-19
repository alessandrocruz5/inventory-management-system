---
type: prompt
plan: "[[2026-11-18-units-stocktakes]]"
id: 03
unit: IMS-28
status: done
model: opus
effort: high
depends_on: [02]
parallel_group: C
files:
  create: []
  modify:
    - src/Modules/Inventory/Inventory/Features/Items/UpdateItem/Handler.cs
    - tests/Inventory.IntegrationTests/ItemsTests.cs
    - src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs
    - tests/Inventory.IntegrationTests/SeedTests.cs
    - docs/Inventory System — Sprint 1 Spec.md
  delete: []
---
# 03 · Base-unit immutability (R3), demo units, spec §5/§9

## Task
Make `PUT /api/items/{id}` reject a base-unit change while the item has a movement or any `ItemUnits` row. Seed three demo units. Record IMS-28 in the spec.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`. Check `git branch --show-current` equals this prompt's `unit` key, and stop if it doesn't. Prompts 01–02 added `ItemUnits` and its API. `CreateItemUnit` reads the item `WITH (UPDLOCK, ROWLOCK)` before inserting.
- Plan D7: today `BaseUnit` is editable after movements (`UpdateItem/Handler.cs:84`), which would silently reinterpret the ledger and the factors.

### 1. `UpdateItem/Handler.cs`
Read `:40-120` first. The SKU check at `:67-75` is the pattern: `HOLDLOCK, FORCESEEK, INDEX([IX_Movements_Item])` on `StockMovements`, inside the strategy transaction. Add, after the SKU check:

```csharp
// Plan D7: the base unit is immutable once the item has a movement or an alternate unit, which would otherwise be
// reinterpreted. Same range lock as the SKU check; on ItemUnits, UPDLOCK + HOLDLOCK over the item's PK slice blocks a
// concurrent unit insert until commit. CreateItemUnit U-locks the item row first, so the two can deadlock (1205); the
// execution strategy retries the loser, as for a SKU edit racing a first posting (TASKS.md IMS-6).
if (!string.Equals(item.BaseUnit, baseUnit, StringComparison.Ordinal)
    && (await db.StockMovements.FromSql($"SELECT * FROM [inventory].[StockMovements] WITH (HOLDLOCK, FORCESEEK, INDEX([IX_Movements_Item])) WHERE [ItemId] = {id}").AnyAsync(ct)
        || await db.ItemUnits.FromSql($"SELECT * FROM [inventory].[ItemUnits] WITH (UPDLOCK, HOLDLOCK, FORCESEEK) WHERE [ItemId] = {id}").AnyAsync(ct)))
{
    errors["baseUnit"] = BaseUnitInUse; // "The base unit can't change once the item has stock history or other units."
    return Problems.Validation(errors);
}
```

- Match the handler's existing variable names (`cancellationToken`, not `ct`).
- If the SKU and base unit both change on an item with movements, return both errors in one 400. Restructure so the SKU and base-unit checks each add to `errors`, sharing one movements query when both changed, then return once.
- Leave every other branch unchanged: 404, duplicate SKU, stale version and the retry recovery.
- `ItemRules.Normalize` lowercases `baseUnit`, so `"KG"` → `"kg"` is not a change.

### 2. `ItemsTests.cs`
Copy `Update_rejects_a_sku_change_once_the_item_has_a_movement` (`:379`):
- `Update_rejects_a_base_unit_change_once_the_item_has_a_movement` → 400 `baseUnit`, with the row unchanged.
- `Update_rejects_a_base_unit_change_once_the_item_has_a_unit`: no movements, a `case` unit → 400 `baseUnit`.
- `Update_allows_a_base_unit_change_with_no_history_or_units` → 200.
- `Update_with_only_a_case_change_in_the_unit_is_not_a_change`: `KG` on a `kg` item with a movement → 200.

### 3. Seed (`Seeding/InventorySeeder.cs`)
Read `:21-140` first. After items are saved and **before** `PostOpeningStockAsync`, insert demo units for the demo tenant:

```csharp
internal static readonly (string Sku, string Code, decimal Factor)[] Units =
[
    ("RICE-WHT", "sack", 25m),
    ("SUGAR-BRN", "sack", 50m),
    ("EGG-MED", "tray", 30m),
];
```

- Look up items by SKU with the same case-insensitive set the seeder already uses. Skip a SKU that no longer exists or was renamed (IMS-9: skip, never fail).
- Skip a pair that already has a row. That makes the seed idempotent by PK.
- Insert `IsActive = true`, `CreatedAt = now`, `CreatedBy = null`. One extra `SaveChangesAsync` is fine.
- Opening stock is unchanged.

### 4. `SeedTests.cs`
Read the single-scenario `[Fact]` at `:31` and extend it in place:
- After the first seed: the demo tenant has exactly these three `ItemUnits` rows.
- After the second seed: still three, unchanged.
- Don't add a second `[Fact]`, because the class owns `SeedTenants.Demo` and leaves QC renamed (IMS-9).

### 5. Spec (`docs/Inventory System — Sprint 1 Spec.md`)
- §5: add the `inventory.ItemUnits` sketch after `Items`, in the same SQL style. Annotate the `Items.BaseUnit` line with `-- immutable once the item has a movement or an alternate unit`. Add a bullet under **Rules**: "An item's alternate units (`ItemUnits`) are fixed factors of its base unit: never edited, deactivated not deleted, and an inactive code stays reserved."
- §9 route table: add `POST /items/{id}/units` · Admin · "Add an alternate unit (`code`, `factor`)" and `POST /items/{id}/units/{code}/deactivate` · Admin · "Deactivate a unit". Amend `GET /items/{id}` to "Item with its balance in each physical location and its units".
- §15 Log: `- 2026-11-18: Sprint 3 (units-stocktakes plan) adds item units (§5, §9) and base-unit immutability.`

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Change `CreateItem`, `CreateItemUnit`, migrations or posting.
- Add dependencies. Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test --project tests/Inventory.IntegrationTests --filter-class '*ItemsTests' --filter-class '*ItemUnitsTests' --filter-class '*SeedTests'
dotnet test --project tests/Platform.IntegrationTests
```
- `Update_rejects_a_sku_change_once_the_item_has_a_movement` still passes unchanged.
