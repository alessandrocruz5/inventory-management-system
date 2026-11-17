using System.Data;

using Microsoft.Data.SqlClient;

namespace Api.Setup;

/// <summary>
/// Creates or updates the <c>inventory_app</c> principal and its schema grants (spec §8): a login-mapped user on SQL
/// Server, or a contained user with a password on Azure SQL Database or a partially contained database. This is
/// principal DDL with no tenant, so it runs on SqlClient rather than EF, and only from the migrate step.
/// </summary>
internal static class DatabasePrincipals
{
    // SQL Server passwords and QUOTENAME's input are both limited to 128 characters.
    private const int MaxPasswordLength = 128;

    // Azure SQL Database (SERVERPROPERTY('EngineEdition')). It can't CREATE LOGIN from a user database.
    private const int AzureSqlDatabaseEdition = 5;

    // The password travels only as @p. QUOTENAME quotes it inside T-SQL, so it's never concatenated in C#.
    private const string LoginSql = """
        DECLARE @q nvarchar(300) = QUOTENAME(@p, '''');
        IF @q IS NULL THROW 50000, 'password too long', 1;
        IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'inventory_app')
            EXEC (N'CREATE LOGIN [inventory_app] WITH PASSWORD = ' + @q + N', CHECK_POLICY = ON;');
        ELSE
            EXEC (N'ALTER LOGIN [inventory_app] WITH PASSWORD = ' + @q + N';');
        IF USER_ID(N'inventory_app') IS NULL
            CREATE USER [inventory_app] FOR LOGIN [inventory_app];
        """;

    // A contained user authenticates at the database, so no server login exists on this path (S14, D5).
    private const string ContainedSql = """
        DECLARE @q nvarchar(300) = QUOTENAME(@p, '''');
        IF @q IS NULL THROW 50000, 'password too long', 1;
        IF USER_ID(N'inventory_app') IS NULL
            EXEC (N'CREATE USER [inventory_app] WITH PASSWORD = ' + @q + N';');
        ELSE
            EXEC (N'ALTER USER [inventory_app] WITH PASSWORD = ' + @q + N';');
        """;

    // Identical on both paths, so T7 holds wherever migrate runs.
    private const string GrantsSql = """
        GRANT SELECT, INSERT, UPDATE ON SCHEMA::[platform]  TO [inventory_app];
        GRANT SELECT, INSERT, UPDATE ON SCHEMA::[identity]  TO [inventory_app];
        GRANT DELETE                 ON SCHEMA::[identity]  TO [inventory_app];
        GRANT SELECT, INSERT, UPDATE ON SCHEMA::[inventory] TO [inventory_app];
        -- The ledger is append-only (spec §6). An object DENY overrides the schema grant above; DELETE too, so a later schema grant can't reach it.
        DENY UPDATE, DELETE ON OBJECT::[inventory].[StockDocuments] TO [inventory_app];
        DENY UPDATE, DELETE ON OBJECT::[inventory].[StockMovements] TO [inventory_app];
        -- NEXT VALUE FOR needs UPDATE on the sequence (spec §8).
        GRANT UPDATE ON OBJECT::[inventory].[MovementIds] TO [inventory_app];
        """;

    public static async Task EnsureAsync(string adminConnectionString, string password, CancellationToken cancellationToken)
    {
        // An nvarchar(128) parameter silently truncates a longer value, so reject it here.
        if (password.Length > MaxPasswordLength)
        {
            throw new InvalidOperationException($"InventoryApp:Password must be at most {MaxPasswordLength} characters");
        }

        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        var principalSql = await IsContainedAsync(connection, cancellationToken) ? ContainedSql : LoginSql;
        await using (var principal = new SqlCommand(principalSql, connection))
        {
            principal.Parameters.Add(new SqlParameter("@p", SqlDbType.NVarChar, MaxPasswordLength) { Value = password });
            await principal.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var grants = new SqlCommand(GrantsSql, connection);
        await grants.ExecuteNonQueryAsync(cancellationToken);
    }

    // Two commands, so the containment query never compiles on Azure SQL Database.
    private static async Task<bool> IsContainedAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var edition = new SqlCommand("SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)", connection))
        {
            if (Convert.ToInt32(await edition.ExecuteScalarAsync(cancellationToken)) == AzureSqlDatabaseEdition)
            {
                return true;
            }
        }

        await using var containment = new SqlCommand("SELECT containment FROM sys.databases WHERE database_id = DB_ID()", connection);
        return Convert.ToInt32(await containment.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}