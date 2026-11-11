using Inventory.Domain;
using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Inventory.Features.Posting.PostIssue;

/// <summary>An issue moves every line from one physical location to the tenant's Customer location, at the moving-average cost.</summary>
internal sealed class PostIssueHandler(DocumentPoster poster)
{
    public Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        PostIssueRequest request,
        HttpContext http,
        CancellationToken cancellationToken) =>
        poster.PostAsync(
            http,
            PostingKind.Issue,
            StockDocumentType.Issue,
            LocationKind.Customer,
            counterpartIsSource: false,
            errors => PostingValidation.Issue(request, errors),
            RequestHash.Issue,
            cancellationToken);
}