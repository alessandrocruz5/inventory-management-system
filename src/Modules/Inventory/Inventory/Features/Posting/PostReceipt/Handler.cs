using Inventory.Domain;
using Inventory.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Inventory.Features.Posting.PostReceipt;

/// <summary>A receipt moves every line from the tenant's Supplier location into one physical location, at its unit cost.</summary>
internal sealed class PostReceiptHandler(DocumentPoster poster)
{
    public Task<Results<Created<DocumentResponse>, Ok<DocumentResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        PostReceiptRequest request,
        HttpContext http,
        CancellationToken cancellationToken) =>
        poster.PostAsync(
            http,
            PostingKind.Receipt,
            StockDocumentType.Receipt,
            LocationKind.Supplier,
            counterpartIsSource: true,
            errors => PostingValidation.Receipt(request, errors),
            RequestHash.Receipt,
            cancellationToken);
}