using System.Net.Http.Json;

using Api.Setup;

using Assistant;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

using Testcontainers.MsSql;

namespace Assistant.Evals;

/// <summary>
/// The real API in-process on a Testcontainers SQL Server 2025, migrated as <c>sa</c> and seeded through the real commands.
/// Every app it creates shares one <see cref="FakeTimeProvider"/>, so the scenario controls every timestamp.
/// </summary>
internal sealed class EvalHost : IAsyncDisposable
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
    private string _appConnectionString = "";

    /// <summary>The shared clock. It starts at 2026-11-02 08:00 Manila, before the seed posts its opening stock.</summary>
    internal FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Random per run and never printed. It meets Identity's policy: upper, lower, digit and symbol.</summary>
    internal string SeedPassword { get; } = "Seed!Passw0rd-" + Guid.NewGuid().ToString("N")[..8];

    private string AppPassword { get; } = "App!Passw0rd-" + Guid.NewGuid().ToString("N")[..8];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _container.StartAsync(cancellationToken);

        var admin = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = "inventory" }.ConnectionString;
        _appConnectionString = new SqlConnectionStringBuilder(admin) { UserID = "inventory_app", Password = AppPassword }.ConnectionString;

        await using (var adminApp = new EvalApiFactory(admin, Clock, null, ("InventoryApp:Password", AppPassword)))
        {
            await MigrateCommand.RunAsync(adminApp.Services, adminApp.Services.GetRequiredService<IConfiguration>(), cancellationToken);
        }

        await using var seedApp = new EvalApiFactory(_appConnectionString, Clock, null, ("Seed:Password", SeedPassword));
        await SeedCommand.RunAsync(seedApp.Services, cancellationToken);
    }

    /// <summary>
    /// An API connected as <c>inventory_app</c>. A non-null <paramref name="model"/> replaces the assistant's model client
    /// and blanks the endpoint, so the row never reaches a real model.
    /// </summary>
    internal EvalApiFactory CreateApp(IChatClient? model, params (string Key, string Value)[] settings)
    {
        // The harness is the only caller, so the per-user rate limit would only get in its way.
        var all = new List<(string Key, string Value)> { ("Assistant:PermitsPerMinute", "10000") };
        all.AddRange(settings);
        if (model is not null)
        {
            all.Add(("Assistant:Endpoint", ""));
        }

        return new EvalApiFactory(_appConnectionString, Clock, model, [.. all]);
    }

    /// <summary>A client with the auth cookie of <paramref name="email"/>. Sign in again after every clock move: the cookie follows the clock.</summary>
    internal async Task<HttpClient> SignInAsync(EvalApiFactory factory, string email, CancellationToken cancellationToken)
    {
        var client = factory.NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = SeedPassword }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return client;
    }

    internal void MoveTo(DateTimeOffset manilaTime) => Clock.SetUtcNow(manilaTime.ToUniversalTime());

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>A copy of the integration tests' <c>ApiFactory</c>: the real API, on the harness clock, with an optional scripted model.</summary>
internal sealed class EvalApiFactory(string connectionString, TimeProvider clock, IChatClient? model, params (string Key, string Value)[] settings)
    : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // This is a console app, not a test assembly, so Mvc.Testing's own content-root lookup misses the API project.
        builder.UseSolutionRelativeContentRoot("src/Api", "*.slnx");
        builder.UseSetting("ConnectionStrings:inventory", connectionString);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton(clock);

            if (model is not null)
            {
                s.RemoveAllKeyed<IChatClient>(AssistantOptions.ModelServiceKey);
                s.AddKeyedSingleton(AssistantOptions.ModelServiceKey, model);
            }
        });
    }

    // https, because the auth cookie is Secure and a CookieContainer won't send it over http.
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });
}