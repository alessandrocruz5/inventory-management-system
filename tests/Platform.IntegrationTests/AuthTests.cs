using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Platform.Identity;

using Shouldly;

namespace Platform.IntegrationTests;

public sealed class AuthTests(DatabaseFixture db)
{
    private const string InvalidCredentialsType = "/problems/invalid-credentials";

    [Fact]
    public async Task Login_sets_an_httponly_secure_strict_cookie()
    {
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        using var client = app.NewClient();

        var response = await Login(client, email, DatabaseFixture.UserPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant();
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("secure");
        cookie.ShouldContain("samesite=strict");
    }

    [Fact]
    public async Task Me_without_a_cookie_is_401_and_not_a_redirect()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        using var client = app.NewClient();

        var response = await client.GetAsync("/api/auth/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Me_returns_id_email_role_and_tenant_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (tenantId, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        using var client = app.NewClient();
        (await Login(client, email, DatabaseFixture.UserPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await client.GetAsync("/api/auth/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        Guid.Parse(me["id"]!.GetValue<string>()).ShouldNotBe(Guid.Empty);
        me["email"]!.GetValue<string>().ShouldBe(email);
        me["role"]!.GetValue<string>().ShouldBe("Staff");
        me["tenantName"]!.GetValue<string>().ShouldBe(DatabaseFixture.TenantName(tenantId));
    }

    [Fact]
    public async Task Login_failures_are_indistinguishable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        var (_, lockedEmail) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        using var client = app.NewClient();

        for (var i = 0; i < 5; i++)
        {
            (await Login(client, lockedEmail, "wrong-password")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var unknown = await Login(client, $"nobody-{Guid.NewGuid():N}@test.local", "wrong-password");
        var wrongPassword = await Login(client, email, "wrong-password");
        var locked = await Login(client, lockedEmail, DatabaseFixture.UserPassword);

        var bodies = new List<string>();
        foreach (var response in new[] { unknown, wrongPassword, locked })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            var body = (await response.Content.ReadFromJsonAsync<JsonObject>(ct))!;
            body["type"]!.GetValue<string>().ShouldBe(InvalidCredentialsType);
            body.Remove("traceId");
            bodies.Add(body.ToJsonString());
        }

        bodies.Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Five_failures_lock_the_account()
    {
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        using var client = app.NewClient();

        for (var i = 0; i < 5; i++)
        {
            (await Login(client, email, "wrong-password")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await Login(client, email, DatabaseFixture.UserPassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        (await users.IsLockedOutAsync(user!)).ShouldBeTrue();
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Staff);
        using var client = app.NewClient();
        (await Login(client, email, DatabaseFixture.UserPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var logout = await client.PostAsync("/api/auth/logout", content: null, ct);
        var me = await client.GetAsync("/api/auth/me", ct);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_rejects_non_json()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        using var client = app.NewClient();

        var response = await client.PostAsync("/api/auth/login", new StringContent("email=a&password=b", System.Text.Encoding.UTF8, "text/plain"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Login_with_empty_fields_is_a_validation_problem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = db.CreateApp();
        using var client = app.NewClient();

        var response = await Login(client, "", "");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>(ct))!;
        body["type"]!.GetValue<string>().ShouldBe("/problems/validation");
        var fields = body["errors"]!.AsObject().Select(e => e.Key.ToLowerInvariant()).ToList();
        fields.ShouldContain("email");
        fields.ShouldContain("password");
    }

    [Fact]
    public async Task Cookie_is_8h_sliding()
    {
        await using var app = db.CreateApp();

        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        options.ExpireTimeSpan.ShouldBe(TimeSpan.FromHours(8));
        options.SlidingExpiration.ShouldBeTrue();
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);
}