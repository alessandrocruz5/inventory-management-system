using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableReadCommittedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Azure SQL Database has RCSI on by default and only lets master change it, so skip when it's already on (S14, D14).
            migrationBuilder.Sql(
                """
                IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) = 0
                BEGIN
                    IF CAST(SERVERPROPERTY('EngineEdition') AS int) = 5 THROW 50000, 'READ_COMMITTED_SNAPSHOT is OFF on this Azure SQL database: turn it on from master, then rerun migrate', 1;
                    EXEC (N'ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;');
                END
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;", suppressTransaction: true);
        }
    }
}
