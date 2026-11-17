using Kernel.Hosting;

using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Platform.Identity;
using Platform.Persistence;
using Platform.Seeding;
using Platform.Tenants;

namespace Platform;

public static class PlatformModuleExtensions
{
    public static IHostApplicationBuilder AddPlatformModule(this IHostApplicationBuilder builder)
    {
        // AddDbContext, never the pooled AddSqlServerDbContext: a pooled context keeps the first request's tenant (spec §8).
        builder.Services.AddDbContext<PlatformDbContext>(o => o.UseSqlServer(
            builder.Configuration.GetConnectionString("inventory"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "platform")));
        builder.EnrichSqlServerDbContext<PlatformDbContext>();
        builder.Services.AddScoped<IModuleMigrator, PlatformMigrator>();
        builder.AddPlatformIdentity();
        builder.Services.AddScoped<TenantProvisioner>();
        builder.Services.AddScoped<IDataSeeder, PlatformSeeder>();
        return builder;
    }

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAuthEndpoints();
        return endpoints;
    }
}