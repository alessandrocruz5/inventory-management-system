namespace Inventory.Features.Locations.UpdateLocation;

internal sealed record UpdateLocationRequest(string? Code, string? Name, byte[]? Version);