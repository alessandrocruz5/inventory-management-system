using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stocktakes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stocktakes",
                schema: "inventory",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)"),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PostedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocktakes", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Stocktakes_PostedDocument", "[PostedDocumentId] IS NULL OR [Status] = 'Posted'");
                    table.CheckConstraint("CK_Stocktakes_Status", "[Status] IN ('Draft', 'Posting', 'Posted', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_Stocktakes_Locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalSchema: "inventory",
                        principalTable: "Locations",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_Stocktakes_StockDocuments_TenantId_PostedDocumentId",
                        columns: x => new { x.TenantId, x.PostedDocumentId },
                        principalSchema: "inventory",
                        principalTable: "StockDocuments",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "StocktakeLines",
                schema: "inventory",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)"),
                    StocktakeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    ExpectedLastMovementId = table.Column<long>(type: "bigint", nullable: true),
                    CountedQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    UnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    CountedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CountedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeLines", x => new { x.TenantId, x.StocktakeId, x.ItemId });
                    table.CheckConstraint("CK_StocktakeLines_Counted", "([CountedQuantity] IS NULL AND [ExpectedQuantity] IS NULL AND [CountedAt] IS NULL AND [CountedBy] IS NULL AND [ExpectedLastMovementId] IS NULL) OR ([CountedQuantity] IS NOT NULL AND [CountedQuantity] >= 0 AND [ExpectedQuantity] IS NOT NULL AND [CountedAt] IS NOT NULL AND [CountedBy] IS NOT NULL)");
                    table.CheckConstraint("CK_StocktakeLines_UnitCost", "[UnitCost] IS NULL OR [UnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_StocktakeLines_Items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_StocktakeLines_Stocktakes_TenantId_StocktakeId",
                        columns: x => new { x.TenantId, x.StocktakeId },
                        principalSchema: "inventory",
                        principalTable: "Stocktakes",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_Created",
                schema: "inventory",
                table: "Stocktakes",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Stocktakes_Open",
                schema: "inventory",
                table: "Stocktakes",
                columns: new[] { "TenantId", "LocationId" },
                unique: true,
                filter: "[Status] IN ('Draft', 'Posting')");

            // Same migration as the tables (spec §8): the policy is schema-bound, so Down drops these before the tables.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY [security].[TenantPolicy]
                    ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Stocktakes],
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Stocktakes] AFTER INSERT,
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Stocktakes] AFTER UPDATE;
                """);

            migrationBuilder.Sql("""
                ALTER SECURITY POLICY [security].[TenantPolicy]
                    ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StocktakeLines],
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StocktakeLines] AFTER INSERT,
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[StocktakeLines] AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY [security].[TenantPolicy]
                    DROP FILTER PREDICATE ON [inventory].[StocktakeLines],
                    DROP BLOCK PREDICATE ON [inventory].[StocktakeLines] AFTER INSERT,
                    DROP BLOCK PREDICATE ON [inventory].[StocktakeLines] AFTER UPDATE;
                """);

            migrationBuilder.Sql("""
                ALTER SECURITY POLICY [security].[TenantPolicy]
                    DROP FILTER PREDICATE ON [inventory].[Stocktakes],
                    DROP BLOCK PREDICATE ON [inventory].[Stocktakes] AFTER INSERT,
                    DROP BLOCK PREDICATE ON [inventory].[Stocktakes] AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "StocktakeLines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "Stocktakes",
                schema: "inventory");
        }
    }
}
