using System.Data;
using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Inventory.Persistence;

/// <summary>
/// Sets the scope's tenant as read-only <c>SESSION_CONTEXT</c> on every connection open, so RLS filters to it (spec §8).
/// Stateless: it reads the tenant from the intercepted context each time, and does nothing when the scope has none.
/// </summary>
internal sealed class TenantSessionInterceptor : DbConnectionInterceptor
{
    public static readonly TenantSessionInterceptor Instance = new();

    private const string SetSessionContextSql =
        "EXEC sp_set_session_context @key = N'" + TenantSchema.SessionKey + "', @value = @t, @read_only = 1;";

    private TenantSessionInterceptor()
    {
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (eventData.Context is InventoryDbContext { Tenant.HasTenant: true } db)
        {
            using var command = CreateCommand(connection, db.Tenant.TenantId);
            command.ExecuteNonQuery();
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is InventoryDbContext { Tenant.HasTenant: true } db)
        {
            await using var command = CreateCommand(connection, db.Tenant.TenantId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static DbCommand CreateCommand(DbConnection connection, Guid tenantId)
    {
        var command = connection.CreateCommand();
        command.CommandText = SetSessionContextSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@t";
        parameter.DbType = DbType.Guid;
        parameter.Value = tenantId;
        command.Parameters.Add(parameter);
        return command;
    }
}