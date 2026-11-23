using System.ComponentModel;

using Assistant.Drafts;

using Kernel.Tenancy;

using Microsoft.Extensions.AI;

namespace Assistant.Tools;

/// <summary>
/// Turns <see cref="InventoryTools"/> into the <see cref="AIFunction"/>s a model may call. The catalog depends on the
/// caller's role (plan D11): the ledger health check is Admin only, and drafting an adjustment is checked by the validator. The role is checked here, server-side, never by the model.
/// </summary>
internal sealed class ToolCatalog(InventoryTools tools, DraftTool draftTool)
{
    /// <summary>A fresh list per call. <paramref name="role"/> is a <see cref="RoleNames"/> value.</summary>
    public IReadOnlyList<AITool> For(string role)
    {
        List<AITool> catalog =
        [
            Make("find_items", "Search items by SKU or name. Returns up to 25.", tools.FindItemsAsync),
            Make("get_item", "One item by SKU: units, and on-hand and value at each location.", tools.GetItemAsync),
            Make("list_locations", "The business's stock locations (codes and names).", tools.ListLocationsAsync),
            Make("get_stock", "On-hand, value and average cost by item and location. Filter by SKU and/or location code. Includes the total value.", tools.GetStockAsync),
            Make("get_item_history", "Recent movements of one item (newest first), optionally at one location, with the balance after each.", tools.GetItemHistoryAsync),
            Make("list_documents", "Posted documents, newest first. Filter by type (Receipt, Issue, Transfer, Adjustment, Reversal) and Manila dates from/to (yyyy-MM-dd, inclusive).", tools.ListDocumentsAsync),
            Make("get_document", "One document by id, with its lines and notes.", tools.GetDocumentAsync),
            Make(
                "draft_document",
                "Prepare a draft receipt, issue, transfer, or adjustment (Admin only) for the user to review and post. It does not post. Quantities and unit costs are per the given unit (default: the item's base unit).",
                (
                    [Description("receipt, issue, transfer or adjustment.")] string type,
                    [Description("The lines. At most 100, one per SKU.")] DraftLineRequest[] lines,
                    [Description("Location code. For a receipt, issue or adjustment.")] string? location = null,
                    [Description("Source location code. Transfer only.")] string? fromLocation = null,
                    [Description("Destination location code. Transfer only.")] string? toLocation = null,
                    [Description("Adjustment only: Damaged, Lost, Found, CountCorrection or Other.")] string? reasonCode = null,
                    [Description("Optional reference, up to 100 characters.")] string? reference = null,
                    [Description("Optional notes, up to 500 characters.")] string? notes = null,
                    CancellationToken ct = default) =>
                    draftTool.DraftAsync(role, type, location, fromLocation, toLocation, reasonCode, reference, notes, lines, ct)),
        ];

        if (string.Equals(role, RoleNames.Admin, StringComparison.Ordinal))
        {
            catalog.Add(Make("get_ledger_health", "Checks the ledger invariants INV-1..INV-5.", tools.GetLedgerHealthAsync));
        }

        return catalog;
    }

    private static AIFunction Make(string name, string description, Delegate tool) =>
        AIFunctionFactory.Create(tool, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = ToolJson.Options,
        });
}