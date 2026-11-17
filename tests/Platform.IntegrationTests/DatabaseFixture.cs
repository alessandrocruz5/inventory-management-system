using Api.Setup;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Platform.Tenants;

using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Platform.IntegrationTests.DatabaseFixture))]

namespace Platform.IntegrationTests;

/// <summary>
/// One SQL Server 2025 container for the whole assembly, migrated once as <c>sa</c> through the real migrate command.
/// Tests then host the API as <c>inventory_app</c>. There is no reset: every test creates its own tenant.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    internal const string UserPassword = "User!Passw0rd-1";

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    internal string AdminConnectionString { get; private set; } = "";

    internal string AppPassword { get; } = "App!Passw0rd-" + Guid.NewGuid().ToString("N")[..8];

    internal string AppConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var admin = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = "inventory" };
        AdminConnectionString = admin.ConnectionString;
        AppConnectionString = new SqlConnectionStringBuilder(AdminConnectionString)
        {
            UserID = "inventory_app",
            Password = AppPassword,
        }.ConnectionString;

        await using (var adminApp = CreateAdminApp())
        {
            await MigrateCommand.RunAsync(adminApp.Services, adminApp.Services.GetRequiredService<IConfiguration>(), CancellationToken.None);
        }

        // Create the roles once up front: concurrent tests would otherwise race to insert the same role.
        await using var app = CreateApp();
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().EnsureRolesAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>An API connected as the server admin, for the migrate step and for reading server metadata.</summary>
    internal ApiFactory CreateAdminApp() => new(AdminConnectionString, ("InventoryApp:Password", AppPassword));

    /// <summary>An API connected as <c>inventory_app</c>, the way it runs for real.</summary>
    internal ApiFactory CreateApp(params (string Key, string Value)[] settings) => new(AppConnectionString, settings);

    internal static async Task<(Guid TenantId, string Email)> CreateTenantWithUserAsync(ApiFactory app, string role)
    {
        var id = Guid.NewGuid();
        var email = $"{role.ToLowerInvariant()}-{id:N}@test.local";

        await using var scope = app.Services.CreateAsyncScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<TenantProvisioner>();
        await provisioner.EnsureRolesAsync(CancellationToken.None);
        await provisioner.CreateAsync(id, TenantName(id), CancellationToken.None);
        await provisioner.EnsureUserAsync(id, email, role, UserPassword, CancellationToken.None);
        return (id, email);
    }

    internal static string TenantName(Guid tenantId) => "T-" + tenantId;
}