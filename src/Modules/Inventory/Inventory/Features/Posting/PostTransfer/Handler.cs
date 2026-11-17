using Inventory.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Inventory.Features.Posting.PostTransfer;

/// <summary>A transfer moves every line from one physical location to another, at the moving-average cost at the source.</summary>
internal sealed class PostTransferHandler(DocumentPoster poster)
{
    public Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        PostTransferRequest request,
        HttpContext http,
        CancellationToken cancellationToken) =>
        poster.PostAsync(
            http,
            PostingKind.Transfer,
            errors => PostingValidation.Transfer(request, errors),
            RequestHash.Transfer,
            cancellationToken);
}