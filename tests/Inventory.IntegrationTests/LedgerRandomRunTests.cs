using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;

using Inventory.Domain;
using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// Spec §11 T9: a seeded, reproducible random run of 500 API operations over 3 items × 3 physical locations, valid and
/// invalid. Every 50 operations, and after a rebuild that runs while a burst of postings continues, INV-1 to INV-5 hold
/// in all three views: the test oracle (<see cref="LedgerInvariants"/>), the product (<c>GET /api/ledger/health</c>)
/// and the projection (<see cref="LedgerProjection"/> against the stored rows and <see cref="CostingPolicy.LastUnitCost"/>).
/// The rebuild's diff is empty. A small model of each physical balance's <c>OnHand</c> and last movement, fed from the
/// <c>201</c> bodies, predicts every status, so the run also checks the status rules.
/// </summary>
public sealed class LedgerRandomRunTests(DatabaseFixture db)
{
    private const int Seed = 20261115;
    private const int Operations = 500;
    private const int CheckpointEvery = 50;
    private const int BurstAt = 250;
    private const int BurstSize = 20;
    private const int HealthChecksDuringBurst = 3;
    private const string Health = "/api/ledger/health";
    private const string Rebuild = "/api/ledger/rebuild";

    private static readonly decimal[] Quantities = [1m, 2m, 3m, 5m, 0.5m, 1.2345m];
    private static readonly decimal[] UnitCosts = [10m, 12.5m, 10.0001m, 99.9999m];
    private static readonly string[] Reasons = ["Damaged", "Lost", "Found", "CountCorrection", "Other"];

    [Fact]
    public async Task Random_run_keeps_every_invariant_and_rebuild_finds_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var elapsed = Stopwatch.StartNew();
        var random = new Random(Seed);
        await using var app = db.CreateApp();
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        using var client = await DatabaseFixture.SignInAsync(app, email);

        var names = new Dictionary<Guid, string>();
        var items = new List<Guid>();
        foreach (var sku in new[] { "I1", "I2", "I3" })
        {
            var id = await PostingTests.NewItemAsync(client, sku, ct);
            items.Add(id);
            names[id] = sku;
        }

        var locations = new List<Guid>();
        foreach (var code in new[] { "L1", "L2", "L3" })
        {
            var id = await PostingTests.NewLocationAsync(client, ct, code);
            locations.Add(id);
            names[id] = code;
        }

        await LocationsTests.AllowNegativeAsync(client, locations[2], true, ct);
        var model = new Model([.. items], [.. locations], allowNegative: locations[2], names);
        var counts = new Dictionary<(string Kind, HttpStatusCode Status, string? Slug), int>();
        await using var connection = await LedgerRows.OpenAsync(db, tenant, ct);

        // Opening stock at L1, so the early operations have something to move.
        foreach (var item in items)
        {
            await StepAsync(0, new Planned(
                "opening", $"opening receipt of {names[item]} 10 @ 10 at L1", PostingTests.Receipts, PostingTests.NewKey(),
                PostingTests.Receipt(locations[0], PostingTests.Line(item, 10m, 10m)), null, new Expected(HttpStatusCode.Created)));
        }

        for (var i = 1; i <= Operations; i++)
        {
            var planned = random.Next(100) switch
            {
                < 22 => PlanReceipt(random, model, "receipt"),
                < 44 => PlanIssue(random, model),
                < 60 => PlanTransfer(random, model),
                < 76 => PlanAdjustment(random, model, "adjustment", random.Next(3)),
                < 88 => PlanReversal(random, model),
                < 95 => PlanReplay(random, model),
                _ => PlanInvalid(random, model),
            };
            await StepAsync(i, planned);

            if (i == BurstAt)
            {
                await BurstWithRebuildAsync(i);
            }

            if (i % CheckpointEvery == 0)
            {
                await CheckpointAsync(i, i == BurstAt ? "checkpoint after the concurrent rebuild" : "checkpoint");
            }
        }

        elapsed.Stop();
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine($"T9 seed {Seed}: {Operations} operations and a burst of {BurstSize} in {elapsed.Elapsed.TotalSeconds:F1} s");
        foreach (var line in counts
            .Select(c => $"T9 count {c.Key.Kind} {(int)c.Key.Status}{(c.Key.Slug is null ? "" : " " + c.Key.Slug)}: {c.Value}")
            .Order(StringComparer.Ordinal))
        {
            output?.WriteLine(line);
        }

        // A run that never exercises an outcome isn't evidence. Burst outcomes don't count.
        var sequential = counts.Keys.Where(k => !k.Kind.StartsWith("burst-", StringComparison.Ordinal)).ToList();
        foreach (var kind in new[] { "receipt", "issue", "transfer", "adjustment", "reversal" })
        {
            sequential.ShouldContain(k => k.Kind == kind && k.Status == HttpStatusCode.Created, $"seed {Seed}: no {kind} 201");
        }

        sequential.ShouldContain(k => k.Status == HttpStatusCode.UnprocessableEntity, $"seed {Seed}: no 422");
        sequential.ShouldContain(k => k.Status == HttpStatusCode.Conflict, $"seed {Seed}: no 409");
        sequential.ShouldContain(k => k.Kind == "replay" && k.Status == HttpStatusCode.OK, $"seed {Seed}: no replay 200");
        sequential.ShouldContain(k => k.Status == HttpStatusCode.BadRequest, $"seed {Seed}: no 400");

        // One sequential operation: send, check its status, and feed a 201 to the model and both pools.
        async Task StepAsync(int i, Planned planned)
        {
            using var response = await SendAsync(client, planned, ct);
            if (await RecordAsync(i, planned, response) is not { } document)
            {
                return;
            }

            var movements = Movements(document);
            model.Apply(movements);
            model.Documents.Add(new Posted(document["id"]!.GetValue<Guid>(), document["type"]!.GetValue<string>(), movements));
            model.Replays.Add(planned);
            if (planned.Kind == "reversal")
            {
                model.Documents.Single(d => d.Id == planned.Reverses).Reversed = true;
            }
        }

        // Checks the status (never 500), the problem type and the replay header; counts the outcome; returns a 201's body.
        async Task<JsonNode?> RecordAsync(int i, Planned planned, HttpResponseMessage response)
        {
            var message = $"seed {Seed}, op {i}, {planned.Description}";
            var text = await response.Content.ReadAsStringAsync(ct);
            response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError, $"{message}: {text}");
            response.StatusCode.ShouldBe(planned.Expected.Status, $"{message}: {text}");
            if (planned.Expected.Slug is { } slug)
            {
                JsonNode.Parse(text)!["type"]!.GetValue<string>().ShouldBe($"/problems/{slug}", $"{message}: {text}");
            }

            if (response.StatusCode == HttpStatusCode.OK)
            {
                response.Headers.TryGetValues("Idempotent-Replayed", out var replayed).ShouldBeTrue(message);
                replayed.ShouldBe(["true"], message);
            }

            var key = (planned.Kind, response.StatusCode, planned.Expected.Slug);
            counts[key] = counts.GetValueOrDefault(key) + 1;
            return response.StatusCode == HttpStatusCode.Created ? JsonNode.Parse(text) : null;
        }

        // Plan D13: only postings whose status doesn't depend on the order the server runs them in go in the burst.
        async Task BurstWithRebuildAsync(int i)
        {
            var burst = Enumerable.Range(1, BurstSize)
                .Select(n => random.Next(2) == 0
                    ? PlanReceipt(random, model, "burst-receipt", n)
                    : PlanAdjustment(random, model, "burst-adjustment", variant: 0, n))
                .ToList();

            var firstInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var postings = burst
                .Select((planned, n) => Task.Run(() =>
                {
                    if (n == 0)
                    {
                        firstInFlight.TrySetResult();
                    }

                    return SendAsync(client, planned, ct);
                }, ct))
                .ToList();
            await firstInFlight.Task;
            var rebuild = Task.Run(() => client.PostAsync(Rebuild, null, ct), ct);
            var healths = Enumerable.Range(0, HealthChecksDuringBurst).Select(_ => Task.Run(() => client.GetAsync(Health, ct), ct)).ToList();
            await Task.WhenAll(postings.Concat(healths).Append(rebuild));

            // Every burst posting is a 201; its request joins the replay pool, its document never the reversal pool.
            for (var n = 0; n < burst.Count; n++)
            {
                using var response = await postings[n];
                var document = (await RecordAsync(i, burst[n], response))!;
                model.Apply(Movements(document));
                model.Replays.Add(burst[n]);
            }

            using (var response = await rebuild)
            {
                var message = $"seed {Seed}, op {i}, rebuild during the burst";
                var text = await response.Content.ReadAsStringAsync(ct);
                response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{message}: {text}");
                JsonNode.Parse(text)!["differences"]!.AsArray().ShouldBeEmpty($"{message}: {text}");
            }

            for (var n = 0; n < healths.Count; n++)
            {
                using var response = await healths[n];
                await AssertHealthyAsync(response, $"seed {Seed}, op {i}, health check {n + 1} during the burst", ct);
            }
        }

        // The three views of INV-1 to INV-5.
        async Task CheckpointAsync(int i, string description)
        {
            var message = $"seed {Seed}, op {i}, {description}";
            try
            {
                await LedgerInvariants.AssertHoldAsync(connection, ct);
            }
            catch (ShouldAssertException e)
            {
                throw new ShouldAssertException($"{message}, oracle: {e.Message}", e);
            }

            using (var response = await client.GetAsync(Health, ct))
            {
                await AssertHealthyAsync(response, $"{message}, health endpoint", ct);
            }

            await AssertProjectionMatchesAsync(app, tenant, connection, $"{message}, projection", ct);
        }
    }

    private static Planned PlanReceipt(Random random, Model model, string kind, int? burst = null)
    {
        var location = Pick(random, model.Locations);
        var lines = DistinctItems(random, model, random.Next(1, 4))
            .Select(item => (Item: item, Quantity: Pick(random, Quantities), UnitCost: Pick(random, UnitCosts)))
            .ToList();
        return new Planned(
            kind,
            $"{Prefix(kind, burst)} at {model.Name(location)}: {string.Join(", ", lines.Select(l => $"{model.Name(l.Item)} {l.Quantity} @ {l.UnitCost}"))}",
            PostingTests.Receipts,
            PostingTests.NewKey(),
            PostingTests.Receipt(location, [.. lines.Select(l => PostingTests.Line(l.Item, l.Quantity, l.UnitCost))]),
            null,
            new Expected(HttpStatusCode.Created));
    }

    /// <summary>Sometimes more than on hand: <c>422</c> at L1 or L2, an estimate <c>201</c> at L3.</summary>
    private static Planned PlanIssue(Random random, Model model)
    {
        var location = Pick(random, model.Locations);
        var lines = DistinctItems(random, model, random.Next(1, 3))
            .Select(item => (Item: item, Quantity: Quantity(random, model.OnHand(item, location))))
            .ToList();
        var shortLine = !model.AllowsNegative(location) && lines.Any(l => l.Quantity > model.OnHand(l.Item, location));
        return new Planned(
            "issue",
            $"issue at {model.Name(location)}: {string.Join(", ", lines.Select(l => $"{model.Name(l.Item)} {l.Quantity} of {model.OnHand(l.Item, location)}"))}",
            PostingTests.Issues,
            PostingTests.NewKey(),
            PostingTests.Issue(location, [.. lines.Select(l => PostingTests.IssueLine(l.Item, l.Quantity))]),
            null,
            shortLine ? new Expected(HttpStatusCode.UnprocessableEntity, "insufficient-stock") : new Expected(HttpStatusCode.Created));
    }

    private static Planned PlanTransfer(Random random, Model model)
    {
        var from = Pick(random, model.Locations);
        var to = Pick(random, [.. model.Locations.Where(l => l != from)]);
        var item = Pick(random, model.Items);
        var onHand = model.OnHand(item, from);
        var quantity = Quantity(random, onHand);
        return new Planned(
            "transfer",
            $"transfer {model.Name(from)} → {model.Name(to)}: {model.Name(item)} {quantity} of {onHand}",
            TransferTests.Transfers,
            PostingTests.NewKey(),
            TransferTests.Transfer(from, to, TransferTests.TransferLine(item, quantity)),
            null,
            !model.AllowsNegative(from) && quantity > onHand
                ? new Expected(HttpStatusCode.UnprocessableEntity, "insufficient-stock")
                : new Expected(HttpStatusCode.Created));
    }

    /// <param name="variant">0: <c>+</c> with a unit cost; 1: <c>+</c> at the average, <c>400</c> when on hand ≤ 0; 2: <c>−</c>.</param>
    private static Planned PlanAdjustment(Random random, Model model, string kind, int variant, int? burst = null)
    {
        var location = Pick(random, model.Locations);
        var item = Pick(random, model.Items);
        var reason = Pick(random, Reasons);
        var onHand = model.OnHand(item, location);
        var (delta, unitCost, expected) = variant switch
        {
            0 => (Pick(random, Quantities), (decimal?)Pick(random, UnitCosts), new Expected(HttpStatusCode.Created)),
            1 => (Pick(random, Quantities), (decimal?)null, onHand > 0m
                ? new Expected(HttpStatusCode.Created)
                : new Expected(HttpStatusCode.BadRequest, "validation")),
            _ => Remove(Quantity(random, onHand)),
        };
        return new Planned(
            kind,
            $"{Prefix(kind, burst)} {reason} at {model.Name(location)}: {model.Name(item)} {delta:+0.####;-0.####}{(unitCost is { } c ? $" @ {c}" : "")} of {onHand}",
            AdjustmentTests.Adjustments,
            PostingTests.NewKey(),
            AdjustmentTests.Adjustment(location, reason, AdjustmentTests.Delta(item, delta, unitCost)),
            null,
            expected);

        (decimal, decimal?, Expected) Remove(decimal quantity) => (
            -quantity,
            null,
            !model.AllowsNegative(location) && quantity > onHand
                ? new Expected(HttpStatusCode.UnprocessableEntity, "insufficient-stock")
                : new Expected(HttpStatusCode.Created));
    }

    /// <summary>Half the time one of the last five documents, which is more often still the latest on its balances.</summary>
    private static Planned PlanReversal(Random random, Model model)
    {
        var documents = model.Documents;
        var original = random.Next(2) == 0
            ? documents[documents.Count - 1 - random.Next(Math.Min(5, documents.Count))]
            : Pick(random, documents);
        return new Planned(
            "reversal",
            $"reversal of {original.Type} {original.Id}",
            ReversalTests.Route(original.Id),
            PostingTests.NewKey(),
            null,
            original.Id,
            model.Reversal(original));
    }

    private static Planned PlanReplay(Random random, Model model)
    {
        var original = Pick(random, model.Replays);
        return original with
        {
            Kind = "replay",
            Description = $"replay of {original.Description}",
            Expected = new Expected(HttpStatusCode.OK),
        };
    }

    /// <summary>A request step 1 rejects whatever the state: quantity 0, or an item that doesn't exist.</summary>
    private static Planned PlanInvalid(Random random, Model model)
    {
        var location = Pick(random, model.Locations);
        if (random.Next(2) == 0)
        {
            var item = Pick(random, model.Items);
            return new Planned(
                "invalid",
                $"invalid receipt at {model.Name(location)}: {model.Name(item)} quantity 0",
                PostingTests.Receipts,
                PostingTests.NewKey(),
                PostingTests.Receipt(location, PostingTests.Line(item, 0m, Pick(random, UnitCosts))),
                null,
                new Expected(HttpStatusCode.BadRequest, "validation"));
        }

        var to = Pick(random, [.. model.Locations.Where(l => l != location)]);
        return new Planned(
            "invalid",
            $"invalid transfer {model.Name(location)} → {model.Name(to)}: unknown item",
            TransferTests.Transfers,
            PostingTests.NewKey(),
            TransferTests.Transfer(location, to, TransferTests.TransferLine(Guid.NewGuid(), Pick(random, Quantities))),
            null,
            new Expected(HttpStatusCode.BadRequest, "validation"));
    }

    private static string Prefix(string kind, int? burst) => burst is { } n ? $"{kind} {n}" : kind;

    private static T Pick<T>(Random random, IReadOnlyList<T> values) => values[random.Next(values.Count)];

    /// <summary>A quantity from the set, or, one time in four, more than <paramref name="onHand"/>.</summary>
    private static decimal Quantity(Random random, decimal onHand) =>
        random.Next(4) == 0 ? Math.Max(onHand, 0m) + Pick(random, Quantities) : Pick(random, Quantities);

    private static List<Guid> DistinctItems(Random random, Model model, int count)
    {
        var pool = model.Items.ToList();
        var chosen = new List<Guid>(count);
        for (var n = 0; n < count; n++)
        {
            var index = random.Next(pool.Count);
            chosen.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return chosen;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, Planned planned, CancellationToken ct) =>
        planned.Body is null
            ? ReversalTests.ReverseAsync(client, planned.Reverses!.Value, planned.Key, ct)
            : PostingTests.PostDocumentAsync(client, planned.Route, planned.Key, planned.Body, ct);

    private static List<Moved> Movements(JsonNode document) =>
        [.. document["movements"]!.AsArray().Select(m => new Moved(
            m!["id"]!.GetValue<long>(),
            m["itemId"]!.GetValue<Guid>(),
            m["fromLocationId"]!.GetValue<Guid>(),
            m["toLocationId"]!.GetValue<Guid>(),
            m["quantity"]!.GetValue<decimal>()))];

    /// <summary>A health response: <c>200</c>, healthy, and all five invariants passed.</summary>
    private static async Task AssertHealthyAsync(HttpResponseMessage response, string message, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{message}: {text}");
        var health = JsonNode.Parse(text)!;
        health["healthy"]!.GetValue<bool>().ShouldBeTrue($"{message}: {text}");
        var invariants = health["invariants"]!.AsArray();
        invariants.Count.ShouldBe(5, $"{message}: {text}");
        foreach (var invariant in invariants)
        {
            invariant!["passed"]!.GetValue<bool>().ShouldBeTrue($"{message}: {text}");
        }
    }

    /// <summary>
    /// Mirrors <c>LedgerProjectionTests.AssertProjectionMatchesAsync</c>: the projection matches the stored balances row
    /// for row, its physical <c>LastUnitCost</c> matches <see cref="CostingPolicy.LastUnitCost"/>, and it has one row per balance.
    /// </summary>
    private static async Task AssertProjectionMatchesAsync(
        ApiFactory app, Guid tenant, SqlConnection connection, string message, CancellationToken ct)
    {
        List<ProjectedBalance> projection;
        await using (var scope = app.Services.GetRequiredService<ITenantScopeFactory>().BeginScope(tenant))
        {
            projection = await LedgerProjection.ReadAsync(scope.Services.GetRequiredService<InventoryDbContext>(), tenant, ct);
        }

        var movements = await LedgerInvariants.MovementsAsync(connection, ct);
        foreach (var row in projection)
        {
            row.Stored.ShouldBe(row.Ledger, $"{message}: {row.Sku} at {row.LocationCode}");
            if (row.IsVirtual)
            {
                continue;
            }

            // Only the item's movements that touch the location: CostingPolicy.LastUnitCost throws on any other.
            var touching = movements
                .Where(m => m.ItemId == row.ItemId && (m.FromLocationId == row.LocationId || m.ToLocationId == row.LocationId))
                .ToList();
            row.Ledger.LastUnitCost.ShouldBe(
                CostingPolicy.LastUnitCost(new BalanceKey(row.ItemId, row.LocationId), touching), $"{message}: {row.Sku} at {row.LocationCode}");
        }

        await using var count = new SqlCommand("SELECT COUNT(*) FROM inventory.StockBalances", connection);
        projection.Count.ShouldBe((int)(await count.ExecuteScalarAsync(ct))!, message);
    }

    /// <summary>One request: a posting when <see cref="Body"/> is set, else a reversal of <see cref="Reverses"/>.</summary>
    private sealed record Planned(string Kind, string Description, string Route, string Key, object? Body, Guid? Reverses, Expected Expected);

    private sealed record Expected(HttpStatusCode Status, string? Slug = null);

    private sealed record Moved(long Id, Guid ItemId, Guid From, Guid To, decimal Quantity);

    private sealed class Posted(Guid id, string type, List<Moved> movements)
    {
        public Guid Id { get; } = id;

        public string Type { get; } = type;

        public List<Moved> Movements { get; } = movements;

        public bool Reversed { get; set; }
    }

    /// <summary>
    /// What the run knows of the ledger, from the <c>201</c> bodies: each physical balance's <c>OnHand</c> and last movement
    /// id. Neither depends on the order the burst posted in, so every status the model predicts is the same in every run.
    /// </summary>
    private sealed class Model(Guid[] items, Guid[] locations, Guid allowNegative, Dictionary<Guid, string> names)
    {
        private readonly Dictionary<(Guid Item, Guid Location), decimal> _onHand = [];
        private readonly Dictionary<(Guid Item, Guid Location), long> _lastMovement = [];

        public IReadOnlyList<Guid> Items => items;

        public IReadOnlyList<Guid> Locations => locations;

        /// <summary>Sequential <c>201</c> documents, reversals included: the reversal pool.</summary>
        public List<Posted> Documents { get; } = [];

        /// <summary>Every <c>201</c> request, the burst's included: the replay pool.</summary>
        public List<Planned> Replays { get; } = [];

        public string Name(Guid id) => names[id];

        public bool AllowsNegative(Guid location) => location == allowNegative;

        public decimal OnHand(Guid item, Guid location) => _onHand.GetValueOrDefault((item, location));

        public void Apply(IEnumerable<Moved> movements)
        {
            foreach (var m in movements)
            {
                if (IsPhysical(m.From))
                {
                    _onHand[(m.ItemId, m.From)] = OnHand(m.ItemId, m.From) - m.Quantity;
                    Touch(m.ItemId, m.From, m.Id);
                }

                if (IsPhysical(m.To))
                {
                    _onHand[(m.ItemId, m.To)] = OnHand(m.ItemId, m.To) + m.Quantity;
                    Touch(m.ItemId, m.To, m.Id);
                }
            }
        }

        /// <summary>The route's checks, then the engine's (spec §6 reversal rules), in the order they run.</summary>
        public Expected Reversal(Posted original)
        {
            if (original.Type == "Reversal")
            {
                return new Expected(HttpStatusCode.UnprocessableEntity, "cannot-reverse-reversal");
            }

            if (original.Reversed)
            {
                return new Expected(HttpStatusCode.Conflict, "already-reversed");
            }

            var keys = original.Movements
                .SelectMany(m => new[] { (m.ItemId, m.From), (m.ItemId, m.To) })
                .Distinct()
                .Where(k => IsPhysical(k.Item2));
            foreach (var (item, location) in keys)
            {
                var latest = original.Movements.Where(m => m.ItemId == item && (m.From == location || m.To == location)).Max(m => m.Id);
                if (_lastMovement[(item, location)] != latest)
                {
                    return new Expected(HttpStatusCode.Conflict, "reversal-not-latest");
                }
            }

            var working = new Dictionary<(Guid, Guid), decimal>();
            foreach (var m in original.Movements.OrderBy(m => m.Id))
            {
                if (IsPhysical(m.To))
                {
                    var onHand = working.TryGetValue((m.ItemId, m.To), out var w) ? w : OnHand(m.ItemId, m.To);
                    if (!AllowsNegative(m.To) && m.Quantity > onHand)
                    {
                        return new Expected(HttpStatusCode.UnprocessableEntity, "insufficient-stock");
                    }

                    working[(m.ItemId, m.To)] = onHand - m.Quantity;
                }

                if (IsPhysical(m.From))
                {
                    working[(m.ItemId, m.From)] = (working.TryGetValue((m.ItemId, m.From), out var w) ? w : OnHand(m.ItemId, m.From)) + m.Quantity;
                }
            }

            return new Expected(HttpStatusCode.Created);
        }

        private bool IsPhysical(Guid location) => locations.Contains(location);

        private void Touch(Guid item, Guid location, long movementId) =>
            _lastMovement[(item, location)] = Math.Max(_lastMovement.GetValueOrDefault((item, location)), movementId);
    }
}