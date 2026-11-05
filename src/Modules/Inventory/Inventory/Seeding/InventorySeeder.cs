using Inventory.Contracts;
using Inventory.Persistence;

using Kernel.Hosting;
using Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Seeding;

/// <summary>The demo tenant's two branches and its retail items. S5d adds opening stock here, through PostingEngine.</summary>
internal sealed class InventorySeeder(ITenantScopeFactory scopes, TimeProvider clock) : IDataSeeder
{
    private static readonly (string Code, string Name)[] Branches =
    [
        ("QC", "Quezon City branch"),
        ("MKT", "Makati branch"),
    ];

    private static readonly (string Sku, string Name, string BaseUnit)[] Items =
    [
        ("RICE-WHT", "White rice", "kg"),
        ("SUGAR-BRN", "Brown sugar", "kg"),
        ("OIL-PALM-1L", "Palm cooking oil 1 L", "pc"),
        ("EGG-MED", "Eggs, medium", "pc"),
        ("NOODLE-CHK", "Instant noodles, chicken", "pc"),
        ("SARDINE-155", "Sardines in tomato sauce 155 g", "pc"),
        ("CORNBEEF-150", "Corned beef 150 g", "pc"),
        ("COFFEE-3IN1", "3-in-1 coffee sachet", "pc"),
        ("MILK-EVAP-370", "Evaporated milk 370 ml", "pc"),
        ("COLA-1.5L", "Cola 1.5 L", "pc"),
        ("WATER-500", "Bottled water 500 ml", "pc"),
        ("WATER-500-24", "Bottled water 500 ml, case of 24", "box"),
        ("SOAP-BAR-90", "Bath soap 90 g", "pc"),
        ("DETERGENT-1KG", "Laundry detergent powder 1 kg", "pc"),
        ("SHAMPOO-12", "Shampoo sachet 12 ml", "pc"),
        ("TOOTHPASTE-100", "Toothpaste 100 ml", "pc"),
        ("VINEGAR-REFILL", "Cane vinegar, refill", "l"),
        ("SOY-1L", "Soy sauce 1 L", "pc"),
        ("PANDESAL-10", "Pandesal, pack of 10", "pc"),
        ("CANDLE-WHT", "Candle, white", "pc"),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.BeginScope(SeedTenants.Demo);
        var db = scope.Services.GetRequiredService<InventoryDbContext>();

        // Case-insensitive like the unique indexes, so a code or SKU renamed only in case isn't re-inserted.
        var locationCodes = (await db.Locations.Select(l => l.Code).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var itemSkus = (await db.Items.Select(i => i.Sku).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = clock.GetUtcNow();

        foreach (var (code, name) in Branches.Where(b => !locationCodes.Contains(b.Code)))
        {
            db.Locations.Add(new Location
            {
                Code = code,
                Name = name,
                Kind = LocationKind.Physical,
                AllowNegativeStock = false,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = null,
            });
        }

        foreach (var (sku, name, baseUnit) in Items.Where(i => !itemSkus.Contains(i.Sku)))
        {
            db.Items.Add(new Item
            {
                Sku = sku,
                Name = name,
                BaseUnit = baseUnit,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = null,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}