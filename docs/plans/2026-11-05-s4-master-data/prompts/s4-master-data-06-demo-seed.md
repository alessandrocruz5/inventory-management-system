---
type: prompt
plan: "[[2026-11-05-s4-master-data]]"
id: 06
unit: IMS-5
status: todo
model: sonnet
effort: medium
depends_on: [01, 02, 05]
parallel_group: F
files:
  create:
    - src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs
  modify:
    - src/Modules/Inventory/Inventory/InventoryModuleExtensions.cs
    - tests/Platform.IntegrationTests/SetupTests.cs
  delete: []
---
# 06 · Demo seed: branches and items

## Task
Add `InventorySeeder`, an `IDataSeeder` that gives the demo tenant two physical branches and 20 retail items. It must be idempotent, so running the seed twice changes nothing. Update the seed-twice test, whose S2 assertions this change breaks.

## Context
Repo root: `/home/apa/Code/portfolio/inventory-management-system`. You are on branch `IMS-5`; don't switch it.
- `Kernel.Hosting.IDataSeeder` has one method, `Task SeedAsync(CancellationToken)`. `Api seed` resolves every `IDataSeeder` in registration order and runs it as `inventory_app` (`src/Api/Setup/SeedCommand.cs:11-14`).
- `Kernel.Hosting.SeedTenants.Demo` is the demo tenant id (prompt 02). `PlatformSeeder.DemoTenantId` aliases it.
- Seed outside a request with `Kernel.Tenancy.ITenantScopeFactory.BeginScope(tenantId)`, which returns an `ITenantScope` (`IAsyncDisposable`, `.Services`). That opens a fresh scope and connection acting as the tenant (IMS-4 Downstream). Copy `Platform/Tenants/TenantProvisioner.cs:30-32` and `Inventory/InventoryModule.cs:17-39`, which is the "insert what's missing, by natural key" shape.
- `inventory.*` has no FK to `platform.Tenants`, so this seeder doesn't depend on Platform's having run. Platform still registers first.
- Entities: `Inventory.Persistence.Location` (`Code`, `Name`, `Kind`, `AllowNegativeStock`, `IsActive`, `CreatedAt`, `CreatedBy` null for system) and `Inventory.Persistence.Item` (`Sku`, `Name`, `BaseUnit`, `IsActive`, `CreatedAt`, `CreatedBy` null when seeded). Never set `TenantId`: the save interceptor stamps it. Set `IsActive = true` explicitly.

**`src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs`:**
```csharp
namespace Inventory.Seeding;

/// <summary>The demo tenant's two branches and its retail items. S5d adds opening stock here, through PostingEngine.</summary>
internal sealed class InventorySeeder(ITenantScopeFactory scopes, TimeProvider clock) : IDataSeeder
```
- `Branches`: `("QC", "Quezon City branch")`, `("MKT", "Makati branch")`, both `LocationKind.Physical` and `AllowNegativeStock = false`.
- `Items` (`Sku`, `Name`, `BaseUnit`), exactly these 20:
  `("RICE-WHT", "White rice", "kg")`, `("SUGAR-BRN", "Brown sugar", "kg")`, `("OIL-PALM-1L", "Palm cooking oil 1 L", "pc")`, `("EGG-MED", "Eggs, medium", "pc")`, `("NOODLE-CHK", "Instant noodles, chicken", "pc")`, `("SARDINE-155", "Sardines in tomato sauce 155 g", "pc")`, `("CORNBEEF-150", "Corned beef 150 g", "pc")`, `("COFFEE-3IN1", "3-in-1 coffee sachet", "pc")`, `("MILK-EVAP-370", "Evaporated milk 370 ml", "pc")`, `("COLA-1.5L", "Cola 1.5 L", "pc")`, `("WATER-500", "Bottled water 500 ml", "pc")`, `("WATER-500-24", "Bottled water 500 ml, case of 24", "box")`, `("SOAP-BAR-90", "Bath soap 90 g", "pc")`, `("DETERGENT-1KG", "Laundry detergent powder 1 kg", "pc")`, `("SHAMPOO-12", "Shampoo sachet 12 ml", "pc")`, `("TOOTHPASTE-100", "Toothpaste 100 ml", "pc")`, `("VINEGAR-REFILL", "Cane vinegar, refill", "l")`, `("SOY-1L", "Soy sauce 1 L", "pc")`, `("PANDESAL-10", "Pandesal, pack of 10", "pc")`, `("CANDLE-WHT", "Candle, white", "pc")`.
- `SeedAsync`: in one `BeginScope(SeedTenants.Demo)`, read the existing location codes and item SKUs, add only the missing ones with `CreatedAt = clock.GetUtcNow()` and `CreatedBy = null`, then call `SaveChangesAsync` once.

**`InventoryModuleExtensions.cs`.** In `AddInventoryModule`, add `builder.Services.AddScoped<IDataSeeder, InventorySeeder>();`. `Kernel.Hosting` is already imported for `IModuleMigrator`.

**`tests/Platform.IntegrationTests/SetupTests.cs`** (`Seed_twice_changes_nothing`, lines 149-197, and `Snapshot`, lines 199-218):
- `Snapshot` returns `(int Tenants, int Users, int Locations, int Items)`. Count `Items` per tenant scope, exactly as it counts `Locations` (lines 205-211).
- Replace `after.Locations.ShouldBe(6);` with `after.Locations.ShouldBe(8);` and add `after.Items.ShouldBe(20);`.
- The per-tenant loop at lines 186-196 now asserts `kinds.ShouldNotContain(LocationKind.Physical)` for both tenants, which breaks. Make it:
  - **demo:** 5 locations; the physical codes are exactly `[MKT, QC]` (ordered); 20 items, all active, including `RICE-WHT` with `BaseUnit` `kg`.
  - **isolation:** 3 locations, no physical, 0 items.
- Keep every other assertion.

## Do not
- Touch files outside the `files` list. If you must, stop and report why.
- Seed the isolation tenant with items or branches, or call any HTTP route from the seeder.
- Add an `Order` property or change `SeedCommand`.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
```
- `Seed_twice_changes_nothing` passes with 8 locations and 20 items, and the rest of `SetupTests` stays green.
- `grep -n 'TenantId' src/Modules/Inventory/Inventory/Seeding/InventorySeeder.cs` prints nothing.
