using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Ledger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "MovementIds",
                schema: "inventory");

            migrationBuilder.CreateTable(
                name: "StockBalances",
                schema: "inventory",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)"),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsVirtual = table.Column<bool>(type: "bit", nullable: false),
                    AllowNegative = table.Column<bool>(type: "bit", nullable: false),
                    OnHand = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    StockValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    LastUnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    LastMovementId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockBalances", x => new { x.TenantId, x.ItemId, x.LocationId });
                    table.CheckConstraint("CK_StockBalances_Physical", "[IsVirtual] = 1 OR ([OnHand] >= 0 AND [StockValue] >= 0 AND ([OnHand] <> 0 OR [StockValue] = 0)) OR ([OnHand] < 0 AND [AllowNegative] = 1)");
                    table.CheckConstraint("CK_StockBalances_VirtualValue", "[IsVirtual] = 0 OR [StockValue] = 0");
                    table.ForeignKey(
                        name: "FK_StockBalances_Items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockBalances_Locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalSchema: "inventory",
                        principalTable: "Locations",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "StockDocuments",
                schema: "inventory",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)"),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ReversesDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReasonCode = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockDocuments", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_StockDocuments_ReasonCode", "([Type] = 'Adjustment' AND [ReasonCode] IS NOT NULL AND [ReasonCode] IN ('Damaged', 'Lost', 'Found', 'CountCorrection', 'Other')) OR ([Type] <> 'Adjustment' AND [ReasonCode] IS NULL)");
                    table.CheckConstraint("CK_StockDocuments_Reversal", "([Type] = 'Reversal' AND [ReversesDocumentId] IS NOT NULL) OR ([Type] <> 'Reversal' AND [ReversesDocumentId] IS NULL)");
                    table.CheckConstraint("CK_StockDocuments_Type", "[Type] IN ('Receipt', 'Issue', 'Transfer', 'Adjustment', 'Reversal')");
                    table.ForeignKey(
                        name: "FK_StockDocuments_StockDocuments_TenantId_ReversesDocumentId",
                        columns: x => new { x.TenantId, x.ReversesDocumentId },
                        principalSchema: "inventory",
                        principalTable: "StockDocuments",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                schema: "inventory",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)"),
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ReversesMovementId = table.Column<long>(type: "bigint", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_StockMovements_Locations", "[FromLocationId] <> [ToLocationId]");
                    table.CheckConstraint("CK_StockMovements_Quantity", "[Quantity] > 0 OR ([Quantity] = 0 AND [TotalCost] > 0)");
                    table.CheckConstraint("CK_StockMovements_TotalCost", "[TotalCost] >= 0");
                    table.ForeignKey(
                        name: "FK_StockMovements_Items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockMovements_Locations_TenantId_FromLocationId",
                        columns: x => new { x.TenantId, x.FromLocationId },
                        principalSchema: "inventory",
                        principalTable: "Locations",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockMovements_Locations_TenantId_ToLocationId",
                        columns: x => new { x.TenantId, x.ToLocationId },
                        principalSchema: "inventory",
                        principalTable: "Locations",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockMovements_StockDocuments_TenantId_DocumentId",
                        columns: x => new { x.TenantId, x.DocumentId },
                        principalSchema: "inventory",
                        principalTable: "StockDocuments",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockMovements_StockMovements_TenantId_ReversesMovementId",
                        columns: x => new { x.TenantId, x.ReversesMovementId },
                        principalSchema: "inventory",
                        principalTable: "StockMovements",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Balances_Location",
                schema: "inventory",
                table: "StockBalances",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "UX_StockDocuments_IdempotencyKey",
                schema: "inventory",
                table: "StockDocuments",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StockDocuments_Reverses",
                schema: "inventory",
                table: "StockDocuments",
                columns: new[] { "TenantId", "ReversesDocumentId" },
                unique: true,
                filter: "[ReversesDocumentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_Item",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "TenantId", "ItemId", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_StockMovements_DocumentLine",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "TenantId", "DocumentId", "LineNo" },
                unique: true);

            // Same migration as the tables (spec §8): the policy is schema-bound, so Down drops these before the tables.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY [security].[TenantPolicy]
                    ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockDocuments],
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockDocuments] AFTER INSERT,
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockDocuments] AFTER UPDATE,
                    ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockMovements],
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockMovements] AFTER INSERT,
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockMovements] AFTER UPDATE,
                    ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockBalances],
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockBalances] AFTER INSERT,
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StockBalances] AFTER UPDATE;
                """);

            // Append-only (spec §6): even the owner can't edit or delete a posted row. Names match HasTrigger in InventoryDbContext.
            migrationBuilder.Sql("""
                CREATE TRIGGER [inventory].[TR_StockDocuments_AppendOnly] ON [inventory].[StockDocuments]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, N'inventory.StockDocuments is append-only. Post a reversal instead.', 1;
                END
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [inventory].[TR_StockMovements_AppendOnly] ON [inventory].[StockMovements]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, N'inventory.StockMovements is append-only. Post a reversal instead.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY [security].[TenantPolicy]
                    DROP FILTER PREDICATE ON [inventory].[StockDocuments],
                    DROP BLOCK PREDICATE ON [inventory].[StockDocuments] AFTER INSERT,
                    DROP BLOCK PREDICATE ON [inventory].[StockDocuments] AFTER UPDATE,
                    DROP FILTER PREDICATE ON [inventory].[StockMovements],
                    DROP BLOCK PREDICATE ON [inventory].[StockMovements] AFTER INSERT,
                    DROP BLOCK PREDICATE ON [inventory].[StockMovements] AFTER UPDATE,
                    DROP FILTER PREDICATE ON [inventory].[StockBalances],
                    DROP BLOCK PREDICATE ON [inventory].[StockBalances] AFTER INSERT,
                    DROP BLOCK PREDICATE ON [inventory].[StockBalances] AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "StockBalances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockMovements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockDocuments",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "MovementIds",
                schema: "inventory");
        }
    }
}
