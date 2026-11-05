namespace Inventory.Persistence;

/// <summary>Names shared by the tenant column default, the session-context interceptor and the RLS policy (spec §8).</summary>
internal static class TenantSchema
{
    /// <summary>The <c>SESSION_CONTEXT</c> key that <c>security.fn_tenant</c> reads.</summary>
    public const string SessionKey = "TenantId";

    /// <summary>Every tenant table's <c>TenantId</c> default, so raw-SQL inserts get the session's tenant.</summary>
    public const string ColumnDefaultSql = "CAST(SESSION_CONTEXT(N'" + SessionKey + "') AS uniqueidentifier)";

    /// <summary>The named query filter on every tenant-owned entity.</summary>
    public const string FilterName = "Tenant";
}