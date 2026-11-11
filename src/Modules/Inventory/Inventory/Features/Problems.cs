using Inventory.Persistence;

using Kernel.Problems;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features;

/// <summary>The ProblemDetails results Inventory endpoints return (spec §9).</summary>
internal static class Problems
{
    public static ValidationProblem Validation(Dictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, type: ProblemTypes.Uri(ProblemTypes.Validation));

    public static ProblemHttpResult NotFound() => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound, title: "Not found", type: ProblemTypes.Uri(ProblemTypes.NotFound));

    public static ProblemHttpResult ConcurrencyConflict() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict, title: "Changed by someone else", type: ProblemTypes.Uri(ProblemTypes.ConcurrencyConflict));

    public static ProblemHttpResult StockNotZero(IEnumerable<StockBalance> balances) => TypedResults.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Stock is not zero",
        type: ProblemTypes.Uri(ProblemTypes.StockNotZero),
        extensions: new Dictionary<string, object?>
        {
            ["balances"] = balances.Select(b => new BlockingBalance(b.ItemId, b.LocationId, b.OnHand, b.StockValue)).ToList(),
        });

    public static ProblemHttpResult IdempotencyKeyMissing() => TypedResults.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Idempotency-Key header missing",
        type: ProblemTypes.Uri(ProblemTypes.IdempotencyKeyMissing));

    public static ProblemHttpResult IdempotencyKeyReused(Guid originalDocumentId) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Idempotency-Key already used for a different request",
        type: ProblemTypes.Uri(ProblemTypes.IdempotencyKeyReused),
        extensions: new Dictionary<string, object?> { ["originalDocumentId"] = originalDocumentId });

    public static ProblemHttpResult InsufficientStock(Guid itemId, Guid locationId, decimal available, decimal requested) => TypedResults.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Insufficient stock",
        type: ProblemTypes.Uri(ProblemTypes.InsufficientStock),
        extensions: new Dictionary<string, object?>
        {
            ["itemId"] = itemId,
            ["locationId"] = locationId,
            ["available"] = available,
            ["requested"] = requested,
        });

    public static ProblemHttpResult InactiveItem(Guid itemId) => TypedResults.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Item is inactive",
        type: ProblemTypes.Uri(ProblemTypes.InactiveItem),
        extensions: new Dictionary<string, object?> { ["itemId"] = itemId });

    public static ProblemHttpResult InactiveLocation(Guid locationId) => TypedResults.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Location is inactive",
        type: ProblemTypes.Uri(ProblemTypes.InactiveLocation),
        extensions: new Dictionary<string, object?> { ["locationId"] = locationId });

    /// <summary>SQL Server 2601 (unique index) or 2627 (unique constraint): a racing insert took the key.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException is SqlException { Number: 2601 or 2627 };
}

/// <summary>A balance that blocks deactivation, as the <c>stock-not-zero</c> problem lists it.</summary>
internal sealed record BlockingBalance(Guid ItemId, Guid LocationId, decimal OnHand, decimal StockValue);