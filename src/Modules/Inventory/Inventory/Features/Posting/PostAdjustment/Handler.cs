using Inventory.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Inventory.Features.Posting.PostAdjustment;

/// <summary>An adjustment moves a positive line from the tenant's Adjustment location to a physical one, and a negative line back.</summary>
internal sealed class PostAdjustmentHandler(DocumentPoster poster)
{
    public Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        PostAdjustmentRequest request,
        HttpContext http,
        CancellationToken cancellationToken) =>
        poster.PostAsync(
            http,
            PostingKind.Adjustment,
            errors => PostingValidation.Adjustment(request, errors),
            RequestHash.Adjustment,
            cancellationToken);
}