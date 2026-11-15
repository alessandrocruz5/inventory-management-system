using Inventory.Domain;
using Inventory.Features.Posting;
using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Reversals.ReverseDocument;

/// <summary>
/// The reversal route (spec §6 reversal rules, plan D3). The order is fixed: the key, the hash, the step 2 replay
/// lookup, then the route's checks, then the engine, so a retry of a successful reversal replays even after the state
/// changed.
/// </summary>
internal sealed class ReverseDocumentHandler(InventoryDbContext db, PostingEngine engine, ICurrentUser user)
{
    public async Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id, HttpContext http, CancellationToken ct)
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

        var hash = RequestHash.Reversal(id);

        // Step 2 before the route's checks (D3).
        if (await engine.FindExistingAsync(key, hash, ct) is { } existing)
        {
            return ToResult(http, existing);
        }

        // Through EF, so the tenant filter and RLS make another tenant's document invisible: 404 (spec §8).
        var original = await db.StockDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        if (original is null)
        {
            return Problems.NotFound();
        }

        if (original.Type == StockDocumentType.Reversal)
        {
            return Problems.CannotReverseReversal(original.ReversesDocumentId
                ?? throw new InvalidOperationException("A reversal document has ReversesDocumentId (CK_StockDocuments_Reversal)."));
        }

        // A racing reversal passes this check and is caught at the engine's step 4 by UX_StockDocuments_Reverses (plan D5).
        var reversal = await db.StockDocuments.AsNoTracking()
            .Where(d => d.ReversesDocumentId == id)
            .Select(d => (Guid?)d.Id)
            .SingleOrDefaultAsync(ct);
        if (reversal is { } reversalId)
        {
            return Problems.AlreadyReversed(reversalId);
        }

        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.DocumentId == id)
            .OrderBy(m => m.Id)
            .Select(m => new LedgerMovement(
                m.Id,
                m.DocumentId,
                m.LineNo,
                m.ItemId,
                m.FromLocationId,
                m.ToLocationId,
                m.Quantity,
                m.TotalCost,
                m.ReversesMovementId))
            .ToListAsync(ct);

        return ToResult(http, await engine.ReverseAsync(new ReversalCommand(id, movements, key, hash, user.UserId), ct));
    }

    private static Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult> ToResult(
        HttpContext http, PostingOutcome outcome)
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
            case AlreadyReversedOutcome alreadyReversed:
                return Problems.AlreadyReversed(alreadyReversed.ReversalDocumentId);
            case ReversalNotLatestOutcome notLatest:
                return Problems.ReversalNotLatest(notLatest.ItemId, notLatest.LocationId, notLatest.BlockingDocumentId);
            case InactiveItemOutcome inactiveItem:
                return Problems.InactiveItem(inactiveItem.ItemId);
            case InactiveLocationOutcome inactiveLocation:
                return Problems.InactiveLocation(inactiveLocation.LocationId);
            case RejectedOutcome { Rejection: InsufficientStockException e }:
                // Reachable once S10's AllowNegative toggle exists.
                return Problems.InsufficientStock(e.ItemId, e.LocationId, e.Available, e.Requested);
            case RetriesExhaustedOutcome:
                return Problems.ConcurrencyConflict();
            default:
                // A reversal never yields BalanceOutOfRangeOutcome or UnitCostRequired.
                throw new InvalidOperationException($"Unexpected reversal outcome: {outcome}.");
        }
    }
}