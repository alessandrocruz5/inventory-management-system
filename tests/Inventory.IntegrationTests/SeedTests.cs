using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Api.Setup;

using Inventory.Domain;
using Inventory.Seeding;

using Kernel.Hosting;

using Microsoft.Data.SqlClient;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// The demo tenant's opening stock (S5d), seeded through the real <c>SeedCommand</c>. One <c>[Fact]</c> runs the whole scenario in order:
/// the rename step mutates the demo tenant for good, and the fixture is shared by the assembly, so separate facts would depend on run order.
/// This is a deliberate exception to spec §11 ("every integration test creates its own tenant"): the seed targets only the demo tenant,
/// and <c>SeedTests</c> is the only class here that uses it.
/// </summary>
public sealed class SeedTests(DatabaseFixture db)
{
    private const int DocumentTotal = 31;
    private const int BalanceTotal = 50;

    [Fact]
    public async Task Seed_posts_opening_stock_once_through_the_engine()
    {
        var ct = TestContext.Current.CancellationToken;

        // 1. First seed.
        await using (var seedApp = db.CreateApp(("Seed:Password", DatabaseFixture.UserPassword)))
        {
            await SeedCommand.RunAsync(seedApp.Services, ct);
        }

        await using var c = await LedgerRows.OpenAsync(db, SeedTenants.Demo, ct);
        var qc = await LedgerRows.LocationIdAsync(c, "QC", ct);
        var mkt = await LedgerRows.LocationIdAsync(c, "MKT", ct);
        var sup = await LedgerRows.LocationIdAsync(c, "SUP", ct);
        var branches = new Dictionary<string, Guid> { ["QC"] = qc, ["MKT"] = mkt };
        var items = await ItemIdsAsync(c, ct);

        // 2. (a) Content.
        await AssertContentAsync(c, branches, items, sup, ct);

        // 3. (b) API replay (R3).
        await using (var app = db.CreateApp())
        {
            using var admin = await DatabaseFixture.SignInAsync(app, "admin@demo.test");
            var body = new
            {
                locationId = qc,
                reference = "Opening stock",
                notes = (string?)null,
                lines = new[] { new { itemId = items["RICE-WHT"], quantity = 120m, unitCost = 48.5m } },
            };

            using var response = await PostingTests.PostDocumentAsync(admin, PostingTests.Receipts, InventorySeeder.OpeningStockKey("RICE-WHT", "QC"), body, ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
            var replayed = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
            var expectedId = await PostingTests.ScalarAsync<Guid>(
                c,
                "SELECT Id FROM inventory.StockDocuments WHERE IdempotencyKey = @key",
                new SqlParameter("@key", SqlDbType.VarChar, 64) { Value = InventorySeeder.OpeningStockKey("RICE-WHT", "QC") },
                ct);
            replayed["id"]!.GetValue<Guid>().ShouldBe(expectedId);
        }

        (await CountAsync(c, "inventory.StockDocuments", ct)).ShouldBe(DocumentTotal);

        // 4. (c) Rename a branch, then seed again (R4).
        var oldBalances = await BalancesAtAsync(c, qc, ct);
        oldBalances.ShouldNotBeEmpty();

        await using (var rename = new SqlCommand("UPDATE inventory.Locations SET Code = N'QC-OLD' WHERE Code = N'QC'", c))
        {
            (await rename.ExecuteNonQueryAsync(ct)).ShouldBe(1);
        }

        await using (var seedApp = db.CreateApp(("Seed:Password", DatabaseFixture.UserPassword)))
        {
            await SeedCommand.RunAsync(seedApp.Services, ct);
        }

        (await CountAsync(c, "inventory.StockDocuments", ct)).ShouldBe(DocumentTotal);
        (await CountAsync(c, "inventory.StockMovements", ct)).ShouldBe(DocumentTotal);

        var newQc = await LedgerRows.LocationIdAsync(c, "QC", ct);
        newQc.ShouldNotBe(qc);
        (await PostingTests.ScalarAsync<string>(
            c,
            "SELECT Kind FROM inventory.Locations WHERE Id = @id",
            new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = newQc },
            ct)).ShouldBe("Physical");
        (await BalancesAtAsync(c, newQc, ct)).ShouldBeEmpty();
        (await BalancesAtAsync(c, qc, ct)).ShouldBe(oldBalances);

        // 5. (d) Isolation.
        await using var isolation = await LedgerRows.OpenAsync(db, SeedTenants.Isolation, ct);
        (await CountAsync(isolation, "inventory.StockDocuments", ct)).ShouldBe(0);
        (await CountAsync(isolation, "inventory.StockBalances", ct)).ShouldBe(0);
    }

    private static async Task AssertContentAsync(
        SqlConnection c, Dictionary<string, Guid> branches, Dictionary<string, Guid> items, Guid sup, CancellationToken ct)
    {
        (await CountAsync(c, "inventory.StockDocuments", ct)).ShouldBe(DocumentTotal);
        (await CountAsync(c, "inventory.StockMovements", ct)).ShouldBe(DocumentTotal);
        (await CountAsync(c, "inventory.StockBalances", ct)).ShouldBe(BalanceTotal);
        InventorySeeder.OpeningStock.Length.ShouldBe(DocumentTotal);

        // Every document is a system-posted opening-stock receipt.
        await using (var shape = new SqlCommand(
            """
            SELECT COUNT(*) FROM inventory.StockDocuments
            WHERE Type = 'Receipt' AND Reference = @reference AND Notes IS NULL
              AND ReversesDocumentId IS NULL AND PostedBy = @postedBy
            """, c))
        {
            shape.Parameters.Add(new SqlParameter("@reference", SqlDbType.NVarChar, 100) { Value = InventorySeeder.OpeningStockReference });
            shape.Parameters.Add(new SqlParameter("@postedBy", SqlDbType.UniqueIdentifier) { Value = InventorySeeder.SystemPrincipal });
            ((int)(await shape.ExecuteScalarAsync(ct))!).ShouldBe(DocumentTotal);
        }

        // The keys, in posting order (movement ids come from a sequence), match the table, case-sensitively.
        var keys = new List<string>();
        await using (var command = new SqlCommand(
            "SELECT d.IdempotencyKey FROM inventory.StockDocuments d JOIN inventory.StockMovements m ON m.DocumentId = d.Id ORDER BY m.Id", c))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                keys.Add(reader.GetString(0));
            }
        }

        var expectedKeys = InventorySeeder.OpeningStock.Select(r => InventorySeeder.OpeningStockKey(r.Sku, r.Code)).ToList();
        keys.ShouldBe(expectedKeys);

        foreach (var (code, sku, quantity, unitCost) in InventorySeeder.OpeningStock)
        {
            var key = InventorySeeder.OpeningStockKey(sku, code);
            var totalCost = CostingPolicy.Round4(quantity * unitCost);
            var movements = await MovementsAsync(c, key, ct);

            var movement = movements.ShouldHaveSingleItem(key);
            movement.LineNo.ShouldBe(1);
            movement.ItemId.ShouldBe(items[sku]);
            movement.From.ShouldBe(sup);
            movement.To.ShouldBe(branches[code]);
            movement.Quantity.ShouldBe(quantity);
            movement.TotalCost.ShouldBe(totalCost);
            movement.Reverses.ShouldBeNull();

            var balance = await BalanceAsync(c, items[sku], branches[code], ct);
            balance.ShouldNotBeNull(key);
            balance.IsVirtual.ShouldBeFalse();
            balance.OnHand.ShouldBe(quantity);
            balance.StockValue.ShouldBe(totalCost);
            balance.LastUnitCost.ShouldBe(unitCost);
            balance.LastMovementId.ShouldBe(movement.Id);
        }

        // Independent oracle: literals worked out by hand, not through the table.
        var rice = (await BalanceAsync(c, items["RICE-WHT"], branches["QC"], ct)).ShouldNotBeNull();
        (rice.OnHand, rice.StockValue, rice.LastUnitCost).ShouldBe((120.0000m, 5820.0000m, (decimal?)48.5000m));
        var vinegar = (await BalanceAsync(c, items["VINEGAR-REFILL"], branches["MKT"], ct)).ShouldNotBeNull();
        (vinegar.OnHand, vinegar.StockValue, vinegar.LastUnitCost).ShouldBe((12.2500m, 426.3000m, (decimal?)34.8000m));

        // CANDLE-WHT has no opening stock.
        (await PostingTests.ScalarAsync<int>(
            c,
            "SELECT COUNT(*) FROM inventory.StockBalances WHERE ItemId = @item",
            new SqlParameter("@item", SqlDbType.UniqueIdentifier) { Value = items["CANDLE-WHT"] },
            ct)).ShouldBe(0);

        // INV-2: for every item the balances net to zero (the Supplier side is the negative of the branches).
        await using var invariant = new SqlCommand(
            "SELECT COUNT(*) FROM (SELECT ItemId FROM inventory.StockBalances GROUP BY ItemId HAVING SUM(OnHand) <> 0) x", c);
        ((int)(await invariant.ExecuteScalarAsync(ct))!).ShouldBe(0);
    }

    private static async Task<int> CountAsync(SqlConnection c, string table, CancellationToken ct)
    {
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM {table}", c);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<Dictionary<string, Guid>> ItemIdsAsync(SqlConnection c, CancellationToken ct)
    {
        var items = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await using var command = new SqlCommand("SELECT Id, Sku FROM inventory.Items", c);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items[reader.GetString(1)] = reader.GetGuid(0);
        }

        return items;
    }

    private static async Task<List<MovementRow>> MovementsAsync(SqlConnection c, string key, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            """
            SELECT m.Id, m.[LineNo], m.ItemId, m.FromLocationId, m.ToLocationId, m.Quantity, m.TotalCost, m.ReversesMovementId
            FROM inventory.StockMovements m
            JOIN inventory.StockDocuments d ON d.Id = m.DocumentId
            WHERE d.IdempotencyKey = @key
            """, c);
        command.Parameters.Add(new SqlParameter("@key", SqlDbType.VarChar, 64) { Value = key });
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<MovementRow>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new MovementRow(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7)));
        }

        return rows;
    }

    private static async Task<BalanceRow?> BalanceAsync(SqlConnection c, Guid item, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            """
            SELECT IsVirtual, OnHand, StockValue, LastUnitCost, LastMovementId
            FROM inventory.StockBalances WHERE ItemId = @item AND LocationId = @location
            """, c);
        command.Parameters.Add(new SqlParameter("@item", SqlDbType.UniqueIdentifier) { Value = item });
        command.Parameters.Add(new SqlParameter("@location", SqlDbType.UniqueIdentifier) { Value = location });
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new BalanceRow(
                reader.GetBoolean(0),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4))
            : null;
    }

    /// <summary>Every balance at the location, as (item, onHand, stockValue, lastMovementId), sorted by item.</summary>
    private static async Task<List<(Guid Item, decimal OnHand, decimal StockValue, long? LastMovementId)>> BalancesAtAsync(
        SqlConnection c, Guid location, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT ItemId, OnHand, StockValue, LastMovementId FROM inventory.StockBalances WHERE LocationId = @location", c);
        command.Parameters.Add(new SqlParameter("@location", SqlDbType.UniqueIdentifier) { Value = location });
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<(Guid, decimal, decimal, long?)>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add((reader.GetGuid(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.IsDBNull(3) ? null : reader.GetInt64(3)));
        }

        return [.. rows.OrderBy(r => r.Item1)];
    }

    private sealed record MovementRow(
        long Id, int LineNo, Guid ItemId, Guid From, Guid To, decimal Quantity, decimal TotalCost, long? Reverses);

    private sealed record BalanceRow(bool IsVirtual, decimal OnHand, decimal StockValue, decimal? LastUnitCost, long? LastMovementId);
}