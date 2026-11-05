using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;
using Platform.Persistence;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>T11: a pooled physical connection never carries the previous tenant (spec §8, §11).</summary>
public sealed class SessionContextTests(DatabaseFixture db)
{
    [Fact]
    public async Task Pooled_connection_never_carries_the_previous_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = CreateSingleConnectionApp();
        var (a, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var scopes = app.Services.GetRequiredService<ITenantScopeFactory>();

        short spidA;
        await using (var scope = scopes.BeginScope(a))
        {
            var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
            spidA = await Spid(inventory, ct);
            // A second open in the same scope: the read-only key must have been reset, then set again.
            (await SessionTenant(inventory, ct)).ShouldBe(a);
        }

        short spidNone;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            spidNone = await Spid(platform, ct);
            (await SessionTenant(platform, ct)).ShouldBeNull();
        }

        short spidB;
        await using (var scope = scopes.BeginScope(b))
        {
            var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
            spidB = await Spid(inventory, ct);
            (await SessionTenant(inventory, ct)).ShouldBe(b);
        }

        spidNone.ShouldBe(spidA, "the test must reuse one physical connection");
        spidB.ShouldBe(spidA, "the test must reuse one physical connection");
    }

    [Fact]
    public async Task Inventory_context_without_a_tenant_sets_no_session_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        await using var scope = app.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        (await SessionTenant(inventory, ct)).ShouldBeNull();
    }

    private ApiFactory CreateSingleConnectionApp() => new(
        new SqlConnectionStringBuilder(db.AppConnectionString)
        {
            MaxPoolSize = 1,
            ApplicationName = "t11-" + Guid.NewGuid().ToString("N")[..8],
        }.ConnectionString);

    private static Task<Guid?> SessionTenant(DbContext context, CancellationToken ct) =>
        context.Database.SqlQuery<Guid?>($"SELECT CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) AS [Value]").SingleAsync(ct);

    private static Task<short> Spid(DbContext context, CancellationToken ct) =>
        context.Database.SqlQuery<short>($"SELECT @@SPID AS [Value]").SingleAsync(ct);
}