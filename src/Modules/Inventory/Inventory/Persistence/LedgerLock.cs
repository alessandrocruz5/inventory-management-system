using System.Data;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Inventory.Persistence;

internal enum LedgerLockMode
{
    Shared,
    Exclusive,
}

/// <summary>
/// The tenant's ledger lock (spec §6 step 3): the applock <c>ledger:&lt;TenantId&gt;</c>. Postings and reversals take
/// it <see cref="LedgerLockMode.Shared"/>; S11's rebuild takes it <see cref="LedgerLockMode.Exclusive"/>.
/// </summary>
internal static class LedgerLock
{
    /// <summary>
    /// Takes the lock, owned by the open transaction, so it must be that transaction's first statement. Every caller
    /// holds no other lock when it asks for this one, so it can't be in a deadlock cycle: <c>-3</c> (deadlock victim) is
    /// unreachable and isn't retried (plan D6). Any result &lt; 0 throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static async Task AcquireAsync(DatabaseFacade database, Guid tenantId, LedgerLockMode mode, CancellationToken ct)
    {
        var lockMode = mode switch
        {
            LedgerLockMode.Shared => "Shared",
            LedgerLockMode.Exclusive => "Exclusive",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

        var lockResult = (await database.SqlQueryRaw<int>(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = @mode, @LockOwner = 'Transaction'; SELECT @r AS [Value];",
                new SqlParameter("@res", SqlDbType.NVarChar, 255) { Value = $"ledger:{tenantId:D}" },
                new SqlParameter("@mode", SqlDbType.VarChar, 32) { Value = lockMode })
            .ToListAsync(ct)).Single();
        if (lockResult < 0)
        {
            throw new InvalidOperationException($"sp_getapplock returned {lockResult} for the ledger lock.");
        }
    }
}