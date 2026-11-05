using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                schema: "inventory",
                table: "Locations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)",
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.EnsureSchema(name: "security");

            migrationBuilder.Sql("""
                CREATE FUNCTION [security].[fn_tenant](@TenantId uniqueidentifier)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN SELECT 1 AS ok
                WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier);
                """);

            // Every later tenant table adds its three predicates with ALTER SECURITY POLICY in the migration that creates it.
            migrationBuilder.Sql("""
                CREATE SECURITY POLICY [security].[TenantPolicy]
                    ADD FILTER PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Locations],
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Locations] AFTER INSERT,
                    ADD BLOCK PREDICATE [security].[fn_tenant]([TenantId]) ON [inventory].[Locations] AFTER UPDATE
                    WITH (STATE = ON, SCHEMABINDING = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY [security].[TenantPolicy];");

            migrationBuilder.Sql("DROP FUNCTION [security].[fn_tenant];");

            migrationBuilder.DropSchema(name: "security");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                schema: "inventory",
                table: "Locations",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValueSql: "CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)");
        }
    }
}
