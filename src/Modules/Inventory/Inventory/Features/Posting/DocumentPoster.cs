using Inventory.Domain;
using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Posting;

/// <summary>
/// The HTTP side of every posting route except reversals (spec §6): receipts, issues, transfers and adjustments. Each
/// moves all its lines between two locations; <see cref="Shape"/> derives everything kind-specific. The order is fixed
/// (plan D2): the key, the pure step-1 checks, the hash, the step 2 replay lookup, then step 1's database checks, then
/// the engine. A replay never runs the database checks, so a retry of a successful post replays even after its master
/// data changed.
/// </summary>
internal sealed class DocumentPoster(InventoryDbContext db, PostingEngine engine, ICurrentUser user)
{
    private static readonly string[] UnknownLocation = ["Choose an existing physical location."];
    private static readonly string[] UnknownItem = ["Choose an existing item."];
    private static readonly string[] BalanceOutOfRange = ["This would exceed the largest stock quantity or value the system can hold."];
    private static readonly string[] UnitCostRequired = ["Enter a unit cost: there is no stock here to average."];

    /// <param name="kind">The posting kind; it fixes the document type, the counterpart and the header locations.</param>
    /// <param name="validate">The pure step-1 checks; null after adding an error per invalid field.</param>
    /// <param name="hash">The <c>RequestHash</c> of the validated request (spec §6 step 2).</param>
    public async Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> PostAsync(
        HttpContext http,
        PostingKind kind,
        Func<Dictionary<string, string[]>, ValidatedPosting?> validate,
        Func<ValidatedPosting, byte[]> hash,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var key = PostingValidation.IdempotencyKey(http.Request.Headers["Idempotency-Key"], errors, out var missing);
        if (missing)
        {
            return Problems.IdempotencyKeyMissing();
        }

        if (key is null)
        {
            return Problems.Validation(errors);
        }

        if (validate(errors) is not { } validated)
        {
            return Problems.Validation(errors);
        }

        var requestHash = hash(validated);

        // Step 2 before the database checks (D2).
        if (await engine.FindExistingAsync(key, requestHash, ct) is { } existing)
        {
            return ToResult(http, kind, validated, existing);
        }

        var (type, counterpart, headers) = Shape(kind, validated);

        // Step 1's database part. Another tenant's ids are invisible through the tenant filter and RLS, so they read as
        // unknown: 400, never 404 or 403 (spec §8).
        var lines = validated.Lines.OrderBy(l => l.LineNo).ToList();
        var locationIds = headers.Select(h => h.Id).ToList();
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        foreach (var (field, id) in headers)
        {
            if (!locations.TryGetValue(id, out var location) || location.Kind != LocationKind.Physical)
            {
                errors[field] = UnknownLocation;
            }
        }

        var itemIds = lines.Select(l => l.ItemId).ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var line in lines.Where(l => !items.ContainsKey(l.ItemId)))
        {
            errors[$"lines[{line.LineNo - 1}].itemId"] = UnknownItem;
        }

        // Every 400 before any 422.
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        // Items, then the header locations in order: the order of the engine's step 7 re-read (items, then each line's
        // From before its To), so a deactivation gives the same problem whichever step sees it.
        if (lines.FirstOrDefault(l => !items[l.ItemId].IsActive) is { } inactive)
        {
            return Problems.InactiveItem(inactive.ItemId);
        }

        foreach (var (_, id) in headers)
        {
            if (!locations[id].IsActive)
            {
                return Problems.InactiveLocation(id);
            }
        }

        // A missing counterpart is a provisioning bug, not a client error: let it throw.
        var counterpartId = counterpart is { } counterpartKind
            ? (await db.Locations.AsNoTracking().SingleAsync(l => l.Kind == counterpartKind, ct)).Id
            : Guid.Empty;
        var locationId = headers[0].Id;

        // The only place an adjustment's signed delta becomes a direction and a positive quantity (plan D2).
        var postingLines = lines
            .Select(l => kind switch
            {
                PostingKind.Receipt => new PostingLine(l.LineNo, l.ItemId, counterpartId, locationId, l.Quantity, l.UnitCost),
                PostingKind.Issue => new PostingLine(l.LineNo, l.ItemId, locationId, counterpartId, l.Quantity, UnitCost: null),
                PostingKind.Transfer => new PostingLine(l.LineNo, l.ItemId, locationId, headers[1].Id, l.Quantity, UnitCost: null),
                PostingKind.Adjustment => l.Quantity > 0
                    ? new PostingLine(l.LineNo, l.ItemId, counterpartId, locationId, l.Quantity, l.UnitCost)
                    : new PostingLine(l.LineNo, l.ItemId, locationId, counterpartId, -l.Quantity, UnitCost: null),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a posting kind."),
            })
            .ToList();

        var command = new PostingCommand(
            kind, type, postingLines, key, requestHash, validated.Reference, validated.Notes, user.UserId, validated.ReasonCode);
        return ToResult(http, kind, validated, await engine.PostAsync(command, ct));
    }

    /// <summary>
    /// Everything kind-specific about a posting: its document type, the virtual counterpart on the other side of every
    /// line (none on a transfer), and the physical header locations to check, as request fields in order.
    /// </summary>
    private static (StockDocumentType Type, LocationKind? Counterpart, (string Field, Guid Id)[] Headers) Shape(
        PostingKind kind, ValidatedPosting validated) =>
        kind switch
        {
            PostingKind.Receipt => (StockDocumentType.Receipt, LocationKind.Supplier, [("locationId", validated.LocationId)]),
            PostingKind.Issue => (StockDocumentType.Issue, LocationKind.Customer, [("locationId", validated.LocationId)]),
            PostingKind.Transfer => (
                StockDocumentType.Transfer,
                null,
                [
                    ("fromLocationId", validated.LocationId),
                    ("toLocationId", validated.ToLocationId ?? throw new InvalidOperationException("A validated transfer has a destination.")),
                ]),
            PostingKind.Adjustment => (StockDocumentType.Adjustment, LocationKind.Adjustment, [("locationId", validated.LocationId)]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a posting kind."),
        };

    private static Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult> ToResult(
        HttpContext http, PostingKind kind, ValidatedPosting validated, PostingOutcome outcome)
    {
        switch (outcome)
        {
            case PostedOutcome { Replayed: false } posted:
                return TypedResults.Created($"/api/documents/{posted.Document.Id}", DocumentResponse.From(posted));
            case PostedOutcome replayed:
                http.Response.Headers["Idempotent-Replayed"] = "true";
                return TypedResults.Ok(DocumentResponse.From(replayed));
            case KeyReusedOutcome reused:
                return Problems.IdempotencyKeyReused(reused.OriginalDocumentId);
            case InactiveItemOutcome inactiveItem:
                return Problems.InactiveItem(inactiveItem.ItemId);
            case InactiveLocationOutcome inactiveLocation:
                return Problems.InactiveLocation(inactiveLocation.LocationId);
            case RejectedOutcome { Rejection: InsufficientStockException e }:
                // On an adjustment, Requested is the positive quantity the engine saw.
                return Problems.InsufficientStock(e.ItemId, e.LocationId, e.Available, e.Requested);
            case RejectedOutcome { Rejection: UnitCostRequiredException e }:
                // LineNo is 1-based; its line is request index LineNo - 1.
                return Problems.Validation(new Dictionary<string, string[]> { [$"lines[{e.LineNo - 1}].unitCost"] = UnitCostRequired });
            case BalanceOutOfRangeOutcome outOfRange:
                var index = validated.Lines.Where(l => l.ItemId == outOfRange.ItemId).Min(l => l.LineNo) - 1;
                var field = kind == PostingKind.Adjustment ? "quantityDelta" : "quantity";
                return Problems.Validation(new Dictionary<string, string[]> { [$"lines[{index}].{field}"] = BalanceOutOfRange });
            case RetriesExhaustedOutcome:
                return Problems.ConcurrencyConflict();
            default:
                // Only ReversalNotLatest is left, and it can't arise from these lines: S9 maps it on its own route.
                throw new InvalidOperationException($"Unexpected posting outcome: {outcome}.");
        }
    }
}