using Inventory.Persistence;

using Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

using Platform.Identity;

using Shouldly;

namespace Inventory.IntegrationTests;

/// <summary>The <c>TenantId</c> stamping interceptor and the model-level guarantees every tenant entity must keep.</summary>
public sealed class TenantInterceptorTests(DatabaseFixture db)
{
    [Fact]
    public async Task Insert_takes_the_scope_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (a, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var scopes = app.Services.GetRequiredService<ITenantScopeFactory>();
        var location = NewLocation();

        await using (var scope = scopes.BeginScope(a))
        {
            var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
            inventory.Locations.Add(location);
            await inventory.SaveChangesAsync(ct);
        }

        await using (var scope = scopes.BeginScope(a))
        {
            var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
            var saved = await inventory.Locations.SingleAsync(l => l.Code == location.Code, ct);
            saved.TenantId.ShouldBe(a);
        }
    }

    [Fact]
    public async Task Insert_for_another_tenant_throws_and_saves_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (a, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var (b, _) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        var scopes = app.Services.GetRequiredService<ITenantScopeFactory>();
        var location = NewLocation();
        location.TenantId = b;

        await using (var scope = scopes.BeginScope(a))
        {
            var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
            inventory.Locations.Add(location);
            await Should.ThrowAsync<InvalidOperationException>(() => inventory.SaveChangesAsync(ct));
        }

        foreach (var tenant in new[] { a, b })
        {
            await using var scope = scopes.BeginScope(tenant);
            var inventory = scope.Services.GetRequiredService<InventoryDbContext>();
            (await inventory.Locations.AnyAsync(l => l.Code == location.Code, ct)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Insert_without_a_tenant_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        await using var scope = app.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        inventory.Locations.Add(NewLocation());

        await Should.ThrowAsync<InvalidOperationException>(() => inventory.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Every_tenant_entity_has_the_tenant_filter_and_default()
    {
        await using var app = db.CreateApp();
        await using var scope = app.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        // The design-time model: the runtime model may drop design-time annotations.
        var model = ((IInfrastructure<IServiceProvider>)inventory).Instance.GetRequiredService<IDesignTimeModel>().Model;
        var tenantEntities = model.GetEntityTypes().Where(e => e.FindProperty("TenantId") is not null).ToList();

        tenantEntities.Select(e => e.ClrType).ShouldContain(typeof(Location));
        foreach (var entity in tenantEntities)
        {
            typeof(ITenantOwned).IsAssignableFrom(entity.ClrType).ShouldBeTrue($"{entity.ClrType.Name} must implement ITenantOwned");
            entity.FindDeclaredQueryFilter(TenantSchema.FilterName).ShouldNotBeNull($"{entity.ClrType.Name} has no \"{TenantSchema.FilterName}\" query filter");
            var tenantId = entity.FindProperty("TenantId")!;
            tenantId.GetDefaultValueSql().ShouldBe(TenantSchema.ColumnDefaultSql, $"{entity.ClrType.Name}.TenantId default");
            tenantId.ValueGenerated.ShouldBe(ValueGenerated.Never, $"{entity.ClrType.Name}.TenantId value generation");
        }
    }

    private static Location NewLocation() => new()
    {
        Code = "I-" + Guid.NewGuid().ToString("N")[..8],
        Name = "Interceptor",
        Kind = LocationKind.Physical,
        CreatedAt = TimeProvider.System.GetUtcNow(),
        IsActive = true,
    };
}