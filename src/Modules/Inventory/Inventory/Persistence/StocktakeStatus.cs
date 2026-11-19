namespace Inventory.Persistence;

/// <summary>
/// A stocktake's state (plan D8, D10). Draft and Posting are open: at most one per location (D12, <c>UX_Stocktakes_Open</c>).
/// Posting freezes the lines while the adjustment posts; Posted and Cancelled are final.
/// </summary>
internal enum StocktakeStatus
{
    Draft,
    Posting,
    Posted,
    Cancelled,
}