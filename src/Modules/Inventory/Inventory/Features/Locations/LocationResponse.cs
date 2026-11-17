using Inventory.Persistence;

namespace Inventory.Features.Locations;

/// <summary>A location as every Locations endpoint returns it; <c>IsSystem</c> marks the virtual SUP, CUS and ADJ rows, and <c>Version</c> is the rowversion an edit sends back.</summary>
internal sealed record LocationResponse(Guid Id, string Code, string Name, string Kind, bool IsSystem, bool AllowNegativeStock, bool IsActive, byte[] Version)
{
    public static LocationResponse From(Location location) => new(
        location.Id,
        location.Code,
        location.Name,
        location.Kind.ToString(),
        location.Kind != LocationKind.Physical,
        location.AllowNegativeStock,
        location.IsActive,
        location.RowVersion);
}