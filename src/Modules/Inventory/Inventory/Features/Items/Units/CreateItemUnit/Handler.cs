using Inventory.Domain;
using Inventory.Features.Posting;
using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Features.Items.Units.CreateItemUnit;

internal sealed class CreateItemUnitHandler(InventoryDbContext db, ICurrentUser user, TimeProvider clock)
{
    private const int MaxCodeLength = 16;

    private static readonly string[] BaseUnitError = ["This is the item's base unit."];
    private static readonly string[] DuplicateError = ["This item already has this unit."];

    public async Task<Results<Created<ItemUnitResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        CreateItemUnitRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var code = (request.Code ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length == 0)
        {
            errors["code"] = ["Code is required."];
        }
        else if (code.Length > MaxCodeLength)
        {
            errors["code"] = [$"Code must be at most {MaxCodeLength} characters."];
        }

        var factor = ValidateFactor(request.Factor, errors);
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        return await db.Database.CreateExecutionStrategy()
            .ExecuteAsync(ct => CreateAsync(id, code, factor, ct), cancellationToken);
    }

    private static decimal ValidateFactor(decimal? value, Dictionary<string, string[]> errors)
    {
        string? message = value switch
        {
            null => "Factor is required.",
            <= 0m => "Factor must be greater than zero.",
            > PostingValidation.MaxAmount => "Factor is too large.",
            { } v when CostingPolicy.Round4(v) != v => "Factor can have at most 4 decimal places.",
            _ => null,
        };

        if (message is not null)
        {
            errors["factor"] = [message];
            return 0m;
        }

        return value!.Value;
    }

    private async Task<Results<Created<ItemUnitResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        Guid id,
        string code,
        decimal factor,
        CancellationToken cancellationToken)
    {
        // A retry starts clean: a row tracked by the failed attempt would hide the fresh read.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Plan D7: the U lock on the item row orders this against a base-unit change (prompt 03), so a unit can't
        // be added against a base unit that is changing at the same time.
        var item = await db.Items
            .FromSql($"SELECT * FROM [inventory].[Items] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            return Problems.NotFound();
        }

        if (code == item.BaseUnit)
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["code"] = BaseUnitError });
        }

        // Inactive units count too: their codes stay reserved (plan D3).
        if (await db.ItemUnits.AnyAsync(u => u.ItemId == id && u.Code == code, cancellationToken))
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["code"] = DuplicateError });
        }

        var unit = new ItemUnit
        {
            ItemId = id,
            Code = code,
            Factor = factor,
            IsActive = true,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user.UserId,
        };
        db.ItemUnits.Add(unit);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (Problems.IsUniqueViolation(ex))
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["code"] = DuplicateError });
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/items/{id}/units/{code}", ItemUnitResponse.From(unit));
    }
}