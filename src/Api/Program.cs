using Api.Setup;
using Api.Tenancy;

using Inventory;

using Kernel.Tenancy;

using Platform;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.AddPlatformModule();
builder.AddInventoryModule();

builder.Services.AddSingleton(TimeProvider.System);

// The interface and the concrete type resolve to the same scoped instance; only Api sets it.
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
builder.Services.AddSingleton<ITenantScopeFactory, TenantScopeFactory>();

var app = builder.Build();

// Run modes: `migrate` runs as the server admin and exits; the serving API never migrates (spec §4).
if (args is ["migrate", ..])
{
    await MigrateCommand.RunAsync(app.Services, app.Configuration, CancellationToken.None);
    return;
}

// `seed` runs as inventory_app and exits.
if (args is ["seed", ..])
{
    await SeedCommand.RunAsync(app.Services, CancellationToken.None);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapPlatformEndpoints();
api.MapInventoryEndpoints();

app.Run();