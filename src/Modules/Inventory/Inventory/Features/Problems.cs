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

    /// <summary>SQL Server 2601 (unique index) or 2627 (unique constraint): a racing insert took the key.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException is SqlException { Number: 2601 or 2627 };
}