using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Inventory.Features.Ledger.RebuildLedger;

internal static class RebuildLedgerEndpoint
{
    /// <summary>
    /// Admin only (spec §9); Staff gets 403. No body and no <c>Idempotency-Key</c>: a rebuild is not a document, and a
    /// second run finds nothing to fix.
    /// </summary>
    public static RouteHandlerBuilder MapRebuildLedger(this RouteGroupBuilder ledger) => ledger.MapPost(
        "/rebuild",
        (RebuildLedgerHandler handler, CancellationToken cancellationToken) => handler.HandleAsync(cancellationToken))
        .RequireAuthorization(InventoryPolicies.Admin);
}