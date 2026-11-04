using Inventory.Contracts;
using Inventory.Persistence;

using Kernel.Hosting;

using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Inventory;

public static class InventoryModuleExtensions
{
    public static IHostApplicationBuilder AddInventoryModule(this IHostApplicationBuilder builder)
    {
        // AddDbContext, never the pooled AddSqlServerDbContext: a pooled context keeps the first request's tenant (spec §8).
        builder.Services.AddDbContext<InventoryDbContext>(o => o.UseSqlServer(
            builder.Configuration.GetConnectionString("inventory"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")));
        builder.EnrichSqlServerDbContext<InventoryDbContext>();
        builder.Services.AddScoped<IModuleMigrator, InventoryMigrator>();
        builder.Services.AddScoped<IInventoryModule, InventoryModule>();
        return builder;
    }

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}