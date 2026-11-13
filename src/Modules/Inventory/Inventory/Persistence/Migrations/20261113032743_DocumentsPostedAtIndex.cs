using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentsPostedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_PostedAt",
                schema: "inventory",
                table: "StockDocuments",
                columns: new[] { "TenantId", "PostedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockDocuments_PostedAt",
                schema: "inventory",
                table: "StockDocuments");
        }
    }
}
