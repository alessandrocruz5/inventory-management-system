using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;

using Inventory.Features.Documents.ListDocuments;
using Inventory.Features.Posting.PostIssue;
using Inventory.Features.Posting.PostReceipt;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>T10 (spec §11): <c>PostedAt</c> comes only from the injected <see cref="TimeProvider"/>, and no request carries a date.</summary>
public sealed class PostingClockTests(DatabaseFixture db)
{
    [Fact]
    public async Task PostedAt_comes_from_the_clock()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 30, 6, 14, 0, TimeSpan.Zero);
        var fake = new FakeTimeProvider(now);
        await using var app = new ApiFactory(db.AppConnectionString) { Clock = fake };
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);
        var location = (await LocationsTests.CreateAsync(client, "WH-1", "Warehouse", ct))["id"]!.GetValue<Guid>();
        var item = (await ItemsTests.CreateAsync(client, "RICE-1", "Rice", "pc", ct))["id"]!.GetValue<Guid>();

        // A client-sent date binds to nothing: no request type has a date property.
        var receipt = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, Guid.NewGuid().ToString("N"), new
        {
            locationId = location,
            reference = (string?)null,
            notes = (string?)null,
            postedAt = "2020-01-01T00:00:00Z",
            lines = new[] { PostingTests.Line(item, 2m, 10m) },
        }, ct);

        receipt.StatusCode.ShouldBe(HttpStatusCode.Created);
        var document = (await receipt.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        document["postedAt"]!.GetValue<DateTimeOffset>().ShouldBe(now);
        document["balancesAsOf"]!.GetValue<DateTimeOffset>().ShouldBe(now);
        var receiptId = document["id"]!.GetValue<Guid>();

        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);
        (await DocumentPostedAtAsync(connection, receiptId, ct)).ShouldBe(now);
        (await MovementPostedAtAsync(connection, receiptId, ct)).ShouldHaveSingleItem().ShouldBe(now);

        fake.Advance(TimeSpan.FromMinutes(5));
        var issue = await PostingTests.PostDocumentAsync(client, PostingTests.Issues, Guid.NewGuid().ToString("N"),
            PostingTests.Issue(location, PostingTests.IssueLine(item, 1m)), ct);

        issue.StatusCode.ShouldBe(HttpStatusCode.Created);
        var later = now.AddMinutes(5);
        var issued = (await issue.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        issued["postedAt"]!.GetValue<DateTimeOffset>().ShouldBe(later);
        issued["balancesAsOf"]!.GetValue<DateTimeOffset>().ShouldBe(later);
        var issueId = issued["id"]!.GetValue<Guid>();
        (await DocumentPostedAtAsync(connection, issueId, ct)).ShouldBe(later);
        (await MovementPostedAtAsync(connection, issueId, ct)).ShouldHaveSingleItem().ShouldBe(later);
    }

    // Read filters bind a date to narrow a list; nothing stores it. Every other request date is banned (CLAUDE.md review constraint).
    private static readonly Dictionary<string, string> AllowedDates = new()
    {
        ["Inventory.Features.Documents.ListDocuments.ListDocumentsRequest.PostedFrom"] = "read filter; never stored",
        ["Inventory.Features.Documents.ListDocuments.ListDocumentsRequest.PostedTo"] = "read filter; never stored",
    };

    [Fact]
    public void No_request_type_carries_a_date()
    {
        Type[] dateTypes = [typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly)];
        var types = typeof(InventoryModuleExtensions).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Inventory.Features", StringComparison.Ordinal) == true)
            .Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal) || t.Name.EndsWith("Line", StringComparison.Ordinal))
            .ToList();

        // The scan must see the posting requests, or it would pass on nothing.
        types.ShouldContain(typeof(PostReceiptRequest));
        types.ShouldContain(typeof(PostIssueRequest));
        types.ShouldContain(typeof(ListDocumentsRequest));

        var dated = types
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => dateTypes.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();
        dated.Where(d => !AllowedDates.ContainsKey(d)).ShouldBeEmpty();
        AllowedDates.Keys.ShouldBeSubsetOf(dated);
    }

    private static async Task<DateTimeOffset> DocumentPostedAtAsync(SqlConnection connection, Guid document, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT PostedAt FROM inventory.StockDocuments WHERE Id = @id", connection);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = document });
        return (DateTimeOffset)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<List<DateTimeOffset>> MovementPostedAtAsync(SqlConnection connection, Guid document, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT PostedAt FROM inventory.StockMovements WHERE DocumentId = @id", connection);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = document });
        await using var reader = await command.ExecuteReaderAsync(ct);
        var postedAt = new List<DateTimeOffset>();
        while (await reader.ReadAsync(ct))
        {
            postedAt.Add(reader.GetDateTimeOffset(0));
        }

        return postedAt;
    }
}