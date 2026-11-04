using System.Data;

using Microsoft.Data.SqlClient;

namespace Api.Setup;

/// <summary>
/// Creates or updates the <c>inventory_app</c> login and user and its schema grants (spec §8). This is server-level
/// DDL with no tenant, so it runs on SqlClient rather than EF, and only from the migrate step.
/// </summary>
internal static class DatabasePrincipals
{
    // SQL Server passwords and QUOTENAME's input are both limited to 128 characters.
    private const int MaxPasswordLength = 128;

    // The password travels only as @p. QUOTENAME quotes it inside T-SQL, so it's never concatenated in C#.
    // S5a adds the sequence grant and DENY UPDATE on the ledger tables.
    private const string EnsureSql = """
        DECLARE @q nvarchar(300) = QUOTENAME(@p, '''');
        IF @q IS NULL THROW 50000, 'password too long', 1;
        IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'inventory_app')
            EXEC (N'CREATE LOGIN [inventory_app] WITH PASSWORD = ' + @q + N', CHECK_POLICY = ON;');
        ELSE
            EXEC (N'ALTER LOGIN [inventory_app] WITH PASSWORD = ' + @q + N';');
        IF USER_ID(N'inventory_app') IS NULL
            CREATE USER [inventory_app] FOR LOGIN [inventory_app];
        GRANT SELECT, INSERT, UPDATE ON SCHEMA::[platform]  TO [inventory_app];
        GRANT SELECT, INSERT, UPDATE ON SCHEMA::[identity]  TO [inventory_app];
        GRANT DELETE                 ON SCHEMA::[identity]  TO [inventory_app];
        GRANT SELECT, INSERT, UPDATE ON SCHEMA::[inventory] TO [inventory_app];
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

        await using var command = new SqlCommand(EnsureSql, connection);
        command.Parameters.Add(new SqlParameter("@p", SqlDbType.NVarChar, MaxPasswordLength) { Value = password });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}