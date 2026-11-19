using Inventory.Persistence;

namespace Inventory.Features.Items;

/// <summary>One alternate unit of an item: <c>Code</c> is <c>Factor</c> base units (plan D3).</summary>
internal sealed record ItemUnitResponse(string Code, decimal Factor, bool IsActive)
{
    public static ItemUnitResponse From(ItemUnit unit) => new(unit.Code, unit.Factor, unit.IsActive);
}