using Microsoft.EntityFrameworkCore;

namespace Inventory.Features;

/// <summary>One page of a list (spec §9: page and pageSize, default 25, max 100).</summary>
internal sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

internal static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>Resolves the defaults, adding an error for a page below 1 or a pageSize outside 1..100.</summary>
    public static (int Page, int PageSize) Resolve(int? page, int? pageSize, Dictionary<string, string[]> errors)
    {
        var resolvedPage = page ?? 1;
        if (resolvedPage < 1)
        {
            errors["page"] = ["Page must be 1 or more."];
        }

        var resolvedPageSize = pageSize ?? DefaultPageSize;
        if (resolvedPageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["Page size must be between 1 and 100."];
        }

        return (resolvedPage, resolvedPageSize);
    }

    /// <summary>Counts, then reads one page. The query must already be ordered.</summary>
    public static async Task<PagedResponse<T>> ToPageAsync<T>(this IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResponse<T>(items, page, pageSize, total);
    }
}