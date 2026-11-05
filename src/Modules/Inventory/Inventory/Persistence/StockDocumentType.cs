namespace Inventory.Persistence;

internal enum StockDocumentType
{
    Receipt,
    Issue,
    Transfer,
    Adjustment,
    Reversal,
}