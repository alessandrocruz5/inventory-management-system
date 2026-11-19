using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemUnits",
                schema: "inventory",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)"),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemUnits", x => new { x.TenantId, x.ItemId, x.Code });
                    table.CheckConstraint("CK_ItemUnits_Factor", "[Factor] > 0");
                    table.ForeignKey(
                        name: "FK_ItemUnits_Items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            // Same migration as the table (spec §8): the policy is schema-bound, so Down drops these before the table.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY [security].[TenantPolicy]
                    ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[ItemUnits],
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[ItemUnits] AFTER INSERT,
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[ItemUnits] AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY [security].[TenantPolicy]
                    DROP FILTER PREDICATE ON [inventory].[ItemUnits],
                    DROP BLOCK PREDICATE ON [inventory].[ItemUnits] AFTER INSERT,
                    DROP BLOCK PREDICATE ON [inventory].[ItemUnits] AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "ItemUnits",
                schema: "inventory");
        }
    }
}
