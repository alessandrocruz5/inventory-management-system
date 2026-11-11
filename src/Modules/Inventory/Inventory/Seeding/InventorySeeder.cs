using Inventory.Contracts;
using Inventory.Domain;
using Inventory.Features.Posting;
using Inventory.Features.Posting.PostReceipt;
using Inventory.Persistence;

using Kernel.Hosting;
using Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Inventory.Seeding;

/// <summary>
/// The demo tenant's two branches and its retail items, then its opening stock: one-line receipts posted through
/// PostingEngine under deterministic idempotency keys (spec §13), so a second run replays and changes nothing.
/// </summary>
internal sealed class InventorySeeder(ITenantScopeFactory scopes, TimeProvider clock, ILogger<InventorySeeder> logger) : IDataSeeder
{
    /// <summary>Who posts the opening stock: no user, the system (plan D2). S7 shows it as "System".</summary>
    internal static readonly Guid SystemPrincipal = Guid.Empty;

    internal const string OpeningStockReference = "Opening stock";

    /// <summary>The demo tenant's opening stock: one receipt per row, in this order (plan D3). CANDLE-WHT has none on purpose.</summary>
    internal static readonly (string Code, string Sku, decimal Quantity, decimal UnitCost)[] OpeningStock =
    [
        ("QC", "RICE-WHT", 120m, 48.50m),
        ("QC", "SUGAR-BRN", 60m, 72.25m),
        ("QC", "OIL-PALM-1L", 48m, 89.75m),
        ("QC", "EGG-MED", 360m, 7.80m),
        ("QC", "NOODLE-CHK", 240m, 9.50m),
        ("QC", "SARDINE-155", 144m, 22.75m),
        ("QC", "CORNBEEF-150", 96m, 41.50m),
        ("QC", "COFFEE-3IN1", 500m, 6.85m),
        ("QC", "MILK-EVAP-370", 72m, 38.25m),
        ("QC", "COLA-1.5L", 48m, 62.00m),
        ("QC", "WATER-500", 120m, 11.50m),
        ("QC", "WATER-500-24", 10m, 235.00m),
        ("QC", "SOAP-BAR-90", 72m, 28.50m),
        ("QC", "DETERGENT-1KG", 36m, 79.95m),
        ("QC", "SHAMPOO-12", 300m, 6.25m),
        ("QC", "TOOTHPASTE-100", 48m, 68.50m),
        ("QC", "VINEGAR-REFILL", 25.5m, 34.80m),
        ("QC", "SOY-1L", 36m, 52.75m),
        ("QC", "PANDESAL-10", 20m, 45.00m),
        ("MKT", "RICE-WHT", 80m, 49.25m),
        ("MKT", "SUGAR-BRN", 40m, 72.25m),
        ("MKT", "OIL-PALM-1L", 24m, 89.75m),
        ("MKT", "EGG-MED", 180m, 7.80m),
        ("MKT", "NOODLE-CHK", 120m, 9.50m),
        ("MKT", "SARDINE-155", 72m, 22.75m),
        ("MKT", "COFFEE-3IN1", 250m, 6.85m),
        ("MKT", "COLA-1.5L", 24m, 62.00m),
        ("MKT", "WATER-500", 96m, 11.50m),
        ("MKT", "SOAP-BAR-90", 36m, 28.50m),
        ("MKT", "SHAMPOO-12", 150m, 6.25m),
        ("MKT", "VINEGAR-REFILL", 12.25m, 34.80m),
    ];

    /// <summary>The deterministic Idempotency-Key of one opening-stock receipt (spec §13): seed:&lt;tenant&gt;:&lt;sku&gt;:&lt;location&gt;.</summary>
    internal static string OpeningStockKey(string sku, string code) => $"seed:{SeedTenants.Demo:D}:{sku}:{code}";

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

        await PostOpeningStockAsync(scope.Services, db, cancellationToken);
    }

    private async Task PostOpeningStockAsync(IServiceProvider services, InventoryDbContext db, CancellationToken ct)
    {
        var engine = services.GetRequiredService<PostingEngine>();

        // Every id is read before the first post: the engine clears the change tracker at the start of each attempt (D7).
        var locations = (await db.Locations.AsNoTracking()
                .Where(l => l.Kind == LocationKind.Physical)
                .Select(l => new { l.Code, l.Id, l.IsActive })
                .ToListAsync(ct))
            .ToDictionary(l => l.Code, l => (l.Id, l.IsActive), StringComparer.OrdinalIgnoreCase);
        var items = (await db.Items.AsNoTracking()
                .Select(i => new { i.Sku, i.Id, i.IsActive })
                .ToListAsync(ct))
            .ToDictionary(i => i.Sku, i => (i.Id, i.IsActive), StringComparer.OrdinalIgnoreCase);

        // A missing Supplier is a provisioning bug, not a seed-data one: let it throw.
        var supplierId = (await db.Locations.AsNoTracking().SingleAsync(l => l.Kind == LocationKind.Supplier, ct)).Id;

        var posted = 0;
        var skipped = 0;
        foreach (var (code, sku, quantity, unitCost) in OpeningStock)
        {
            var key = OpeningStockKey(sku, code);
            if (!locations.TryGetValue(code, out var location) || !items.TryGetValue(sku, out var item))
            {
                logger.LogWarning("Opening stock {Key} skipped: its item or location is missing.", key);
                skipped++;
                continue;
            }

            var errors = new Dictionary<string, string[]>();
            if (PostingValidation.IdempotencyKey(new StringValues(key), errors, out _) is null)
            {
                throw new InvalidOperationException($"The opening stock key {key} is not a valid Idempotency-Key.");
            }

            var validated = PostingValidation.Receipt(
                new PostReceiptRequest(location.Id, OpeningStockReference, null, [new PostReceiptLine(item.Id, quantity, unitCost)]),
                errors);
            if (validated is null)
            {
                throw new InvalidOperationException(
                    $"The opening stock receipt {key} failed validation: {string.Join(", ", errors.Keys)}.");
            }

            var hash = RequestHash.Receipt(validated);

            // D1: a key that already holds a document is already seeded, whatever that document says.
            if (await engine.FindExistingAsync(key, hash, ct) is { } existing)
            {
                if (existing is KeyReusedOutcome reused)
                {
                    logger.LogInformation(
                        "Opening stock {Key} skipped: the key holds document {DocumentId} from a different request (renamed item or branch).",
                        key,
                        reused.OriginalDocumentId);
                }

                skipped++;
                continue;
            }

            // IsActive is not pre-checked: the engine re-checks it under lock at step 7.
            var line = validated.Lines.Single();
            var postingLine = new PostingLine(line.LineNo, line.ItemId, supplierId, validated.LocationId, line.Quantity, line.UnitCost);
            var command = new PostingCommand(
                PostingKind.Receipt, StockDocumentType.Receipt, [postingLine], key, hash, validated.Reference, validated.Notes, SystemPrincipal);

            switch (await engine.PostAsync(command, ct))
            {
                case PostedOutcome { Replayed: false }:
                    posted++;
                    break;
                case PostedOutcome or KeyReusedOutcome:
                    // A parallel seed won the key (D6).
                    skipped++;
                    break;
                case InactiveItemOutcome or InactiveLocationOutcome:
                    logger.LogWarning("Opening stock {Key} skipped: its item or location is inactive.", key);
                    skipped++;
                    break;
                case var outcome:
                    throw new InvalidOperationException($"Opening stock {key} was not posted: {outcome}.");
            }
        }

        logger.LogInformation("Opening stock: {Posted} posted, {Skipped} already seeded or skipped.", posted, skipped);
    }
}