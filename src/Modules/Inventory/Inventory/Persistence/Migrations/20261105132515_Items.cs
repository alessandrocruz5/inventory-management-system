using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Items : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Items",
                schema: "inventory",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)"),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseUnit = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "UX_Items_Sku",
                schema: "inventory",
                table: "Items",
                columns: new[] { "TenantId", "Sku" },
                unique: true);

            // Same migration as the table (spec §8): the policy is schema-bound, so Down drops these before the table.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY [security].[TenantPolicy]
                    ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Items],
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Items] AFTER INSERT,
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Items] AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY [security].[TenantPolicy]
                    DROP FILTER PREDICATE ON [inventory].[Items],
                    DROP BLOCK PREDICATE ON [inventory].[Items] AFTER INSERT,
                    DROP BLOCK PREDICATE ON [inventory].[Items] AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "Items",
                schema: "inventory");
        }
    }
}
