using Inventory.Domain;
using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Posting;

/// <summary>
/// The HTTP side of a posting that moves every line between one physical location and the tenant's counterpart
/// (spec §6): receipts and issues, and S6 later. The order is fixed (plan D2): the key, the pure step-1 checks, the
/// hash, the step 2 replay lookup, then step 1's database checks, then the engine. A replay never runs the database
/// checks, so a retry of a successful post replays even after its master data changed.
/// </summary>
internal sealed class DocumentPoster(InventoryDbContext db, PostingEngine engine, ICurrentUser user)
{
    private static readonly string[] UnknownLocation = ["Choose an existing physical location."];
    private static readonly string[] UnknownItem = ["Choose an existing item."];
    private static readonly string[] BalanceOutOfRange = ["This would exceed the largest stock quantity or value the system can hold."];

    /// <param name="counterpart">The virtual location on the other side of every line: Supplier, Customer, ….</param>
    /// <param name="counterpartIsSource">
    /// True when stock flows from the counterpart into the location (a receipt, which carries unit costs); false when
    /// it flows out of the location to the counterpart (an issue, costed at the moving average).
    /// </param>
    /// <param name="validate">The pure step-1 checks; null after adding an error per invalid field.</param>
    /// <param name="hash">The <c>RequestHash</c> of the validated request (spec §6 step 2).</param>
    public async Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> PostAsync(
        HttpContext http,
        PostingKind kind,
        StockDocumentType type,
        LocationKind counterpart,
        bool counterpartIsSource,
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
            return ToResult(http, validated, existing);
        }

        // Step 1's database part. Another tenant's ids are invisible through the tenant filter and RLS, so they read as
        // unknown: 400, never 404 or 403 (spec §8).
        var lines = validated.Lines.OrderBy(l => l.LineNo).ToList();
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == validated.LocationId, ct);
        if (location is not { Kind: LocationKind.Physical })
        {
            errors["locationId"] = UnknownLocation;
        }

        var itemIds = lines.Select(l => l.ItemId).ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var line in lines.Where(l => !items.ContainsKey(l.ItemId)))
        {
            errors[$"lines[{line.LineNo - 1}].itemId"] = UnknownItem;
        }

        // Every 400 before any 422.
        if (errors.Count > 0 || location is null)
        {
            return Problems.Validation(errors);
        }

        // Items, then the location: the order of the engine's step 7 re-read, so a deactivation gives the same problem
        // whichever step sees it.
        if (lines.FirstOrDefault(l => !items[l.ItemId].IsActive) is { } inactive)
        {
            return Problems.InactiveItem(inactive.ItemId);
        }

        if (!location.IsActive)
        {
            return Problems.InactiveLocation(location.Id);
        }

        // A missing counterpart is a provisioning bug, not a client error: let it throw.
        var counterpartId = (await db.Locations.AsNoTracking().SingleAsync(l => l.Kind == counterpart, ct)).Id;
        var postingLines = lines
            .Select(l => counterpartIsSource
                ? new PostingLine(l.LineNo, l.ItemId, counterpartId, location.Id, l.Quantity, l.UnitCost)
                : new PostingLine(l.LineNo, l.ItemId, location.Id, counterpartId, l.Quantity, UnitCost: null))
            .ToList();

        var command = new PostingCommand(kind, type, postingLines, key, requestHash, validated.Reference, validated.Notes, user.UserId);
        return ToResult(http, validated, await engine.PostAsync(command, ct));
    }

    private static Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult> ToResult(
        HttpContext http, ValidatedPosting validated, PostingOutcome outcome)
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
                return Problems.InsufficientStock(e.ItemId, e.LocationId, e.Available, e.Requested);
            case BalanceOutOfRangeOutcome outOfRange:
                var index = validated.Lines.Where(l => l.ItemId == outOfRange.ItemId).Min(l => l.LineNo) - 1;
                return Problems.Validation(new Dictionary<string, string[]> { [$"lines[{index}].quantity"] = BalanceOutOfRange });
            case RetriesExhaustedOutcome:
                return Problems.ConcurrencyConflict();
            default:
                // UnitCostRequired and ReversalNotLatest can't arise from these lines; S6 and S9 map them on their routes.
                throw new InvalidOperationException($"Unexpected posting outcome: {outcome}.");
        }
    }
}