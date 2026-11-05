using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Inventory.IntegrationTests;

/// <summary>Hosts the real API against the test container. Starting it never migrates: only an explicit migrate call does.</summary>
internal sealed class ApiFactory(string connectionString, params (string Key, string Value)[] settings)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:inventory", connectionString);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
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