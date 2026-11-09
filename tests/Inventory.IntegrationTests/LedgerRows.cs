using System.Data;
using System.Diagnostics;

using Microsoft.Data.SqlClient;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>
/// Inserts ledger rows as <c>inventory_app</c> on a raw connection whose session context is the tenant;
/// <c>TenantId</c> comes from the column default.
/// </summary>
internal static class LedgerRows
{
    private static readonly TimeSpan BlockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>A raw connection as <c>inventory_app</c>, unpooled, with the tenant as a read-only session key.</summary>
    public static async Task<SqlConnection> OpenAsync(DatabaseFixture db, Guid tenant, CancellationToken ct)
    {
        var connection = new SqlConnection(new SqlConnectionStringBuilder(db.AppConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = Command(connection, null,
            "EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1;",
            ("@t", SqlDbType.UniqueIdentifier, tenant));
        await command.ExecuteNonQueryAsync(ct);
        return connection;
    }

    /// <summary>The tenant's location with this code: SUP, CUS, ADJ or a physical code.</summary>
    public static async Task<Guid> LocationIdAsync(SqlConnection c, string code, CancellationToken ct, SqlTransaction? tx = null)
    {
        await using var command = Command(c, tx, "SELECT Id FROM inventory.Locations WHERE Code = @code",
            ("@code", SqlDbType.NVarChar, code));
        return (Guid)(await command.ExecuteScalarAsync(ct))!;
    }

    public static async Task<Guid> AddItemAsync(SqlConnection c, string sku, CancellationToken ct, SqlTransaction? tx = null)
    {
        await using var command = Command(c, tx, """
            DECLARE @id uniqueidentifier = NEWID();
            INSERT INTO inventory.Items (Id, Sku, Name, BaseUnit, CreatedAt)
            VALUES (@id, @sku, @sku, N'pc', SYSDATETIMEOFFSET());
            SELECT @id;
            """,
            ("@sku", SqlDbType.NVarChar, sku));
        return (Guid)(await command.ExecuteScalarAsync(ct))!;
    }

    public static async Task<Guid> AddLocationAsync(SqlConnection c, string code, CancellationToken ct, bool allowNegative = false, SqlTransaction? tx = null)
    {
        await using var command = Command(c, tx, """
            DECLARE @id uniqueidentifier = NEWID();
            INSERT INTO inventory.Locations (Id, Code, Name, Kind, AllowNegativeStock, CreatedAt)
            VALUES (@id, @code, @code, 'Physical', @allowNegative, SYSDATETIMEOFFSET());
            SELECT @id;
            """,
            ("@code", SqlDbType.NVarChar, code),
            ("@allowNegative", SqlDbType.Bit, allowNegative));
        return (Guid)(await command.ExecuteScalarAsync(ct))!;
    }

    public static async Task<Guid> AddDocumentAsync(SqlConnection c, CancellationToken ct, SqlTransaction? tx = null,
        string type = "Receipt", Guid? reverses = null, string? reasonCode = null, string? idempotencyKey = null)
    {
        await using var command = Command(c, tx, """
            DECLARE @id uniqueidentifier = NEWID();
            INSERT INTO inventory.StockDocuments (Id, Type, ReversesDocumentId, ReasonCode, IdempotencyKey, RequestHash, PostedAt, PostedBy)
            VALUES (@id, @type, @reverses, @reason, @key, HASHBYTES('SHA2_256', @key), SYSDATETIMEOFFSET(), NEWID());
            SELECT @id;
            """,
            ("@type", SqlDbType.VarChar, type),
            ("@reverses", SqlDbType.UniqueIdentifier, reverses),
            ("@reason", SqlDbType.VarChar, reasonCode),
            ("@key", SqlDbType.VarChar, idempotencyKey ?? Guid.NewGuid().ToString()));
        return (Guid)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Takes the id first, as S5c does: a table with an enabled trigger refuses <c>OUTPUT</c> without <c>INTO</c>.</summary>
    public static async Task<long> AddMovementAsync(SqlConnection c, Guid documentId, int lineNo, Guid itemId, Guid from, Guid to,
        decimal quantity, decimal totalCost, CancellationToken ct, SqlTransaction? tx = null, long? reverses = null)
    {
        await using var command = Command(c, tx, """
            DECLARE @id bigint = NEXT VALUE FOR [inventory].[MovementIds];
            INSERT INTO inventory.StockMovements
                (Id, DocumentId, [LineNo], ItemId, FromLocationId, ToLocationId, Quantity, TotalCost, ReversesMovementId, PostedAt, PostedBy)
            VALUES (@id, @document, @line, @item, @from, @to, @quantity, @cost, @reverses, SYSDATETIMEOFFSET(), NEWID());
            SELECT @id;
            """,
            ("@document", SqlDbType.UniqueIdentifier, documentId),
            ("@line", SqlDbType.Int, lineNo),
            ("@item", SqlDbType.UniqueIdentifier, itemId),
            ("@from", SqlDbType.UniqueIdentifier, from),
            ("@to", SqlDbType.UniqueIdentifier, to),
            ("@quantity", SqlDbType.Decimal, quantity),
            ("@cost", SqlDbType.Decimal, totalCost),
            ("@reverses", SqlDbType.BigInt, reverses));
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public static async Task AddBalanceAsync(SqlConnection c, Guid itemId, Guid locationId, bool isVirtual, bool allowNegative,
        decimal onHand, decimal stockValue, CancellationToken ct, SqlTransaction? tx = null)
    {
        await using var command = Command(c, tx, """
            INSERT INTO inventory.StockBalances (ItemId, LocationId, IsVirtual, AllowNegative, OnHand, StockValue)
            VALUES (@item, @location, @isVirtual, @allowNegative, @onHand, @value);
            """,
            ("@item", SqlDbType.UniqueIdentifier, itemId),
            ("@location", SqlDbType.UniqueIdentifier, locationId),
            ("@isVirtual", SqlDbType.Bit, isVirtual),
            ("@allowNegative", SqlDbType.Bit, allowNegative),
            ("@onHand", SqlDbType.Decimal, onHand),
            ("@value", SqlDbType.Decimal, stockValue));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Waits (10 s at most) until a request is blocked by <paramref name="holder"/>'s session, polling sys.dm_exec_requests as sa.</summary>
    public static async Task WaitUntilBlockedAsync(DatabaseFixture db, SqlConnection holder, CancellationToken ct, SqlTransaction? tx = null)
    {
        int spid;
        await using (var command = Command(holder, tx, "SELECT @@SPID"))
        {
            spid = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }

        await using var admin = new SqlConnection(db.AdminConnectionString);
        await admin.OpenAsync(ct);
        await using var poll = Command(admin, null, "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = @spid",
            ("@spid", SqlDbType.Int, spid));

        var waited = Stopwatch.StartNew();
        var blocked = 0;
        while (waited.Elapsed < BlockTimeout)
        {
            blocked = (int)(await poll.ExecuteScalarAsync(ct))!;
            if (blocked > 0)
            {
                return;
            }

            await Task.Delay(PollInterval, ct);
        }

        blocked.ShouldBeGreaterThan(0, $"No request was blocked by session {spid} within {BlockTimeout.TotalSeconds} s");
    }

    private static SqlCommand Command(SqlConnection connection, SqlTransaction? tx, string sql, params (string Name, SqlDbType Type, object? Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection, tx);
        foreach (var (name, type, value) in parameters)
        {
            var parameter = new SqlParameter(name, type) { Value = value ?? DBNull.Value };
            switch (type)
            {
                case SqlDbType.Decimal:
                    parameter.Precision = 19;
                    parameter.Scale = 4;
                    break;
                case SqlDbType.VarChar:
                    parameter.Size = 64;
                    break;
            }

            command.Parameters.Add(parameter);
        }

        return command;
    }
}