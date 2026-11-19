using System.Globalization;
using System.Text.RegularExpressions;

using Inventory.Domain;
using Inventory.Features.Posting;
using Inventory.Persistence;

using Kernel.Problems;
using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Inventory.Features.Stocktakes.PostStocktake;

/// <summary>
/// Posts a stocktake's snapshot deltas as one <c>CountCorrection</c> adjustment (plan D9, D10, D13, D14). The lines are
/// frozen while the status is Posting, so every attempt builds the same posting and hash; the key <c>stocktake:&lt;id&gt;</c>
/// lets the engine post it once only; the status records the outcome, and a crash between post and finish heals on a retry.
/// </summary>
internal sealed partial class PostStocktakeHandler(
    InventoryDbContext db,
    DocumentPoster poster,
    ICurrentUser user,
    TimeProvider clock,
    StocktakeReader reader,
    ILogger<PostStocktakeHandler> logger)
{
    private static readonly string MaxAmountText = PostingValidation.MaxAmount.ToString("N4", CultureInfo.InvariantCulture);
    private static readonly string[] NoLines = ["Add at least one count."];
    private static readonly string[] Uncounted = ["Count this item, or enter 0."];
    private static readonly string[] DifferenceTooLarge = [$"The difference from the expected quantity must be at most {MaxAmountText}."];
    private static readonly string[] TooManyDifferences =
        [$"More than {PostingValidation.MaxLines} counts differ; a stocktake can post at most {PostingValidation.MaxLines} corrections."];

    public async Task<Results<Ok<StocktakeDetailResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        // 1. Freeze: Draft -> Posting after the D13 checks, or continue a Posting one (a crashed or parallel attempt).
        var (failure, frozen) = await strategy.ExecuteAsync(ct => FreezeAsync(id, ct), cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        // Already Posted: nothing to do, so posting again is idempotent.
        if (frozen is null)
        {
            return await DetailAsync(id, cancellationToken);
        }

        // 2. Build: the non-zero deltas in sheet order (D14), numbered 1..n. Index is the line's position on the sheet.
        var differences = frozen.Lines
            .Select((line, index) => (line.ItemId, line.UnitCost, Delta: Delta(line), Index: index))
            .Where(d => d.Delta != 0m)
            .ToList();

        Guid? documentId = null;
        if (differences.Count > 0)
        {
            var validated = new ValidatedPosting(
                frozen.LocationId,
                $"Stocktake {id.ToString("N")[..8].ToUpperInvariant()}",
                frozen.Notes,
                [.. differences.Select((d, n) => new ValidatedLine(n + 1, d.ItemId, d.Delta, d.Delta > 0m ? d.UnitCost : null))])
            {
                ReasonCode = AdjustmentReason.CountCorrection,
            };

            // 3. Post under the server's key. Clients can't send this prefix (PostingValidation.IdempotencyKey).
            var key = PostingValidation.StocktakeKeyPrefix + id.ToString("D");
            var result = await poster.PostValidatedAsync(
                http, PostingKind.Adjustment, key, validated, RequestHash.Adjustment(validated), cancellationToken);
            switch (result.Result)
            {
                case Created<DocumentResponse> created:
                    documentId = created.Value!.Id;
                    break;
                case Ok<DocumentResponse> replayed:
                    documentId = replayed.Value!.Id;
                    break;
                case ProblemHttpResult reused when reused.ProblemDetails.Type == ProblemTypes.Uri(ProblemTypes.IdempotencyKeyReused):
                    // Only this route posts under the key, so its document is this stocktake's adjustment, built from other
                    // lines: a parallel attempt failed and unfroze, and the sheet was recounted before this retry. Record it;
                    // throwing would leave the stocktake in Posting, where counting and cancelling are refused, for good.
                    documentId = (Guid)reused.ProblemDetails.Extensions["originalDocumentId"]!;
                    logger.LogWarning(
                        "Stocktake {StocktakeId}: document {DocumentId} holds its key with another request hash; recording it.",
                        id,
                        documentId);
                    break;
                case ValidationProblem invalid:
                    await strategy.ExecuteAsync(ct => UnfreezeAsync(id, ct), cancellationToken);
                    return Problems.Validation(ToSheetKeys(invalid.ProblemDetails.Errors, [.. differences.Select(d => d.Index)]));
                case ProblemHttpResult problem:
                    // Nothing was written: insufficient stock, an inactive item or location, or exhausted retries.
                    await strategy.ExecuteAsync(ct => UnfreezeAsync(id, ct), cancellationToken);
                    return problem;
                default:
                    throw new InvalidOperationException($"Unexpected posting result: {result.Result}.");
            }
        }

        // 4. Finish.
        await strategy.ExecuteAsync(ct => FinishAsync(id, documentId, ct), cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    private async Task<(Results<Ok<StocktakeDetailResponse>, ValidationProblem, ProblemHttpResult>? Failure, Frozen? Frozen)> FreezeAsync(
        Guid id, CancellationToken cancellationToken)
    {
        // A retry starts clean: a row tracked by the failed attempt would hide the fresh read.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // D15: the U lock that counting and cancelling take, so no count can land between the checks and the freeze.
        var stocktake = await LockAsync(id, cancellationToken);
        if (stocktake is null)
        {
            return (Problems.NotFound(), null);
        }

        if (stocktake.Status == StocktakeStatus.Posted)
        {
            return (null, null);
        }

        if (stocktake.Status == StocktakeStatus.Cancelled)
        {
            return (Problems.StocktakeNotOpen(stocktake.Status), null);
        }

        var lines = await SheetLinesAsync(id, cancellationToken);

        // Posting: checked when it froze, and counts are refused since, so the lines are the frozen ones.
        if (stocktake.Status == StocktakeStatus.Draft)
        {
            if (Check(lines) is { } errors)
            {
                return (Problems.Validation(errors), null);
            }

            stocktake.Status = StocktakeStatus.Posting;
            Touch(stocktake);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return (null, new Frozen(stocktake.LocationId, stocktake.Notes, lines));
    }

    /// <summary>The post wrote nothing: back to Draft so the counts can be fixed, unless another attempt already moved it on.</summary>
    private async Task UnfreezeAsync(Guid id, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (await LockAsync(id, cancellationToken) is not { Status: StocktakeStatus.Posting } stocktake)
        {
            return;
        }

        stocktake.Status = StocktakeStatus.Draft;
        Touch(stocktake);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Records the outcome. A document in the ledger is recorded whatever the status, because a parallel attempt that failed
    /// may have unfrozen the stocktake meanwhile; without one, only a Posting stocktake becomes Posted.
    /// </summary>
    private async Task FinishAsync(Guid id, Guid? documentId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // No hard deletes, so the row the freeze saw is still there.
        var stocktake = await LockAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Stocktake {id} is missing after it froze.");

        // A parallel attempt finished first.
        if (stocktake.Status == StocktakeStatus.Posted && (documentId is null || stocktake.PostedDocumentId == documentId))
        {
            return;
        }

        if (documentId is null && stocktake.Status != StocktakeStatus.Posting)
        {
            return;
        }

        stocktake.Status = StocktakeStatus.Posted;
        stocktake.PostedDocumentId = documentId;
        Touch(stocktake);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Results<Ok<StocktakeDetailResponse>, ValidationProblem, ProblemHttpResult>> DetailAsync(
        Guid id, CancellationToken cancellationToken) =>
        await reader.ReadAsync(id, cancellationToken) is { } detail ? TypedResults.Ok(detail) : Problems.NotFound();

    private Task<Stocktake?> LockAsync(Guid id, CancellationToken cancellationToken) =>
        db.Stocktakes
            .FromSql($"SELECT * FROM [inventory].[Stocktakes] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>The stocktake's lines in sheet order (D14), the order error keys index into.</summary>
    private async Task<IReadOnlyList<SheetLine>> SheetLinesAsync(Guid id, CancellationToken cancellationToken)
    {
        var lines = await (
            from line in db.StocktakeLines.AsNoTracking()
            where line.StocktakeId == id
            join item in db.Items on line.ItemId equals item.Id
            select new SheetLine(line.ItemId, item.Sku, line.ExpectedQuantity, line.CountedQuantity, line.UnitCost))
            .ToListAsync(cancellationToken);
        return StocktakeReader.InLineOrder(lines, l => l.Sku);
    }

    /// <summary>D13: every line counted, at least one line, each delta in range, and at most MaxLines differences.</summary>
    private static Dictionary<string, string[]>? Check(IReadOnlyList<SheetLine> lines)
    {
        var errors = new Dictionary<string, string[]>();
        if (lines.Count == 0)
        {
            errors["lines"] = NoLines;
        }

        var differences = 0;
        for (var j = 0; j < lines.Count; j++)
        {
            if (lines[j].Counted is null)
            {
                errors[$"lines[{j}].quantity"] = Uncounted;
                continue;
            }

            var delta = Delta(lines[j]);
            if (Math.Abs(delta) > PostingValidation.MaxAmount)
            {
                errors[$"lines[{j}].quantity"] = DifferenceTooLarge;
            }

            if (delta != 0m)
            {
                differences++;
            }
        }

        if (differences > PostingValidation.MaxLines)
        {
            errors["lines"] = TooManyDifferences;
        }

        return errors.Count > 0 ? errors : null;
    }

    /// <summary>
    /// D14: the poster's keys index the document's lines; the client knows the sheet's. <c>lines[i].unitCost</c> and
    /// <c>lines[i].quantityDelta</c> become <c>lines[j].unitCost</c> and <c>lines[j].quantity</c>; other keys pass through.
    /// </summary>
    private static Dictionary<string, string[]> ToSheetKeys(IDictionary<string, string[]> errors, IReadOnlyList<int> sheetIndex)
    {
        var remapped = new Dictionary<string, string[]>();
        foreach (var (key, messages) in errors)
        {
            var match = PosterLineKey().Match(key);
            if (!match.Success)
            {
                remapped[key] = messages;
                continue;
            }

            var j = sheetIndex[int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture)];
            remapped[$"lines[{j}].{(match.Groups["field"].Value == "unitCost" ? "unitCost" : "quantity")}"] = messages;
        }

        return remapped;
    }

    // Counted lines only: the freeze refuses an uncounted one, and CK_StocktakeLines_Counted sets both together.
    private static decimal Delta(SheetLine line) =>
        line.Counted is { } counted ? counted - line.Expected!.Value : 0m;

    private void Touch(Stocktake stocktake)
    {
        stocktake.UpdatedAt = clock.GetUtcNow();
        stocktake.UpdatedBy = user.UserId;
    }

    [GeneratedRegex(@"^lines\[(?<index>\d+)\]\.(?<field>unitCost|quantityDelta)$", RegexOptions.CultureInvariant)]
    private static partial Regex PosterLineKey();

    private sealed record SheetLine(Guid ItemId, string Sku, decimal? Expected, decimal? Counted, decimal? UnitCost);

    private sealed record Frozen(Guid LocationId, string? Notes, IReadOnlyList<SheetLine> Lines);
}