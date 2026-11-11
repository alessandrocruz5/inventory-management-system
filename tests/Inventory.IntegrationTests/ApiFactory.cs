using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inventory.IntegrationTests;

/// <summary>Hosts the real API against the test container. Starting it never migrates: only an explicit migrate call does.</summary>
internal sealed class ApiFactory(string connectionString, params (string Key, string Value)[] settings)
    : WebApplicationFactory<Program>
{
    /// <summary>When set, replaces the API's <see cref="TimeProvider"/>, so a test controls every timestamp (T10).</summary>
    internal TimeProvider? Clock { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:inventory", connectionString);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        if (Clock is { } clock)
        {
            builder.ConfigureTestServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton(clock);
            });
        }
    }

    // https, because the auth cookie is Secure and a CookieContainer won't send it over http.
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });
}