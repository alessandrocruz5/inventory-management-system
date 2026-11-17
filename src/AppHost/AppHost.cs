var builder = DistributedApplication.CreateBuilder(args);

// `aspire publish` target (S13): writes deploy/docker-compose.yaml. Ignored by `dotnet run`.
builder.AddDockerComposeEnvironment("compose");

var sql = builder.AddSqlServer("sql")
    .WithImageTag("2025-latest")
    .WithDataVolume();

var db = sql.AddDatabase("inventory");

var appPassword = builder.AddParameter("inventory-app-password",
    new GenerateParameterDefault { MinLength = 24, Special = false, MinLower = 1, MinUpper = 1, MinNumeric = 1 },
    secret: true, persist: true);
var seedPassword = builder.AddParameter("seed-password",
    new GenerateParameterDefault { MinLength = 16, MinLower = 1, MinUpper = 1, MinNumeric = 1, MinSpecial = 1 },
    secret: true, persist: true);

var appConnection = builder.AddConnectionString("inventory-app", ReferenceExpression.Create(
    $"Server={sql.Resource.Host},{sql.Resource.Port};Database=inventory;User ID=inventory_app;Password={appPassword};TrustServerCertificate=true"));

var migrate = builder.AddProject<Projects.Api>("migrate", launchProfileName: null)
    .WithArgs("migrate")
    .WithReference(db) // server admin: migrate only
    .WithEnvironment("InventoryApp__Password", appPassword)
    .WaitFor(db);

var seed = builder.AddProject<Projects.Api>("seed", launchProfileName: null)
    .WithArgs("seed")
    .WithReference(appConnection, "inventory") // inventory_app
    .WithEnvironment("Seed__Password", seedPassword)
    .WaitForCompletion(migrate);

var api = builder.AddProject<Projects.Api>("api")
    .WithReference(appConnection, "inventory") // inventory_app, never db
    .WaitForCompletion(seed)
    .WithHttpHealthCheck("/health");

builder.AddJavaScriptApp("web", "../../web", "start")
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints()
    // Publish only: web/Dockerfile serves the production build with nginx and proxies /api, so the browser keeps one origin (spec §8).
    .PublishAsDockerFile(c => c.WithEntrypoint("/docker-entrypoint.sh").WithArgs("nginx", "-g", "daemon off;")); // the nginx image's own entrypoint and CMD, restated because compose resets CMD when entrypoint is set

builder.Build().Run();