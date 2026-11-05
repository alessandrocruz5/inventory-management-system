using Inventory.Contracts;
using Inventory.Features.Items;
using Inventory.Features.Items.CreateItem;
using Inventory.Features.Items.DeactivateItem;
using Inventory.Features.Items.GetItem;
using Inventory.Features.Items.ListItems;
using Inventory.Features.Items.UpdateItem;
using Inventory.Features.Locations;
using Inventory.Features.Locations.CreateLocation;
using Inventory.Features.Locations.DeactivateLocation;
using Inventory.Features.Locations.ListLocations;
using Inventory.Features.Locations.UpdateLocation;
using Inventory.Persistence;
using Inventory.Seeding;

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
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
            .AddInterceptors(TenantSessionInterceptor.Instance, TenantSaveChangesInterceptor.Instance));
        builder.EnrichSqlServerDbContext<InventoryDbContext>();
        builder.Services.AddScoped<IModuleMigrator, InventoryMigrator>();
        builder.Services.AddScoped<IInventoryModule, InventoryModule>();
        builder.Services.AddScoped<IDataSeeder, InventorySeeder>();
        builder.Services.AddScoped<ListItemsHandler>();
        builder.Services.AddScoped<GetItemHandler>();
        builder.Services.AddScoped<CreateItemHandler>();
        builder.Services.AddScoped<UpdateItemHandler>();
        builder.Services.AddScoped<DeactivateItemHandler>();
        builder.Services.AddScoped<ListLocationsHandler>();
        builder.Services.AddScoped<CreateLocationHandler>();
        builder.Services.AddScoped<UpdateLocationHandler>();
        builder.Services.AddScoped<DeactivateLocationHandler>();
        return builder;
    }

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapItemEndpoints();
        endpoints.MapLocationEndpoints();
        return endpoints;
    }
}