using System.Security.Claims;

using Api.Tenancy;

using Kernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Platform.IntegrationTests;

public sealed class TenantMiddlewareTests
{
    [Fact]
    public async Task Authenticated_principal_without_tenant_claim_gets_401()
    {
        var context = NewContext();
        context.User = Principal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Email, "x@test.local"),
            new Claim(ClaimTypes.Role, "Staff"));
        var tenant = new TenantContext();
        var nextCalled = false;
        var middleware = new TenantMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, tenant, new CurrentUser());

        context.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        nextCalled.ShouldBeFalse();
        tenant.HasTenant.ShouldBeFalse();
    }

    [Fact]
    public async Task Anonymous_request_passes_through()
    {
        var context = NewContext();
        var nextCalled = false;
        var middleware = new TenantMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, new TenantContext(), new CurrentUser());

        nextCalled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Tenant_claim_fills_the_context()
    {
        var tenantId = Guid.NewGuid();
        var context = NewContext();
        context.User = Principal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Email, "x@test.local"),
            new Claim(ClaimTypes.Role, "Staff"),
            new Claim(TenantClaims.TenantId, tenantId.ToString()));
        var tenant = new TenantContext();
        var nextCalled = false;
        var middleware = new TenantMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, tenant, new CurrentUser());

        nextCalled.ShouldBeTrue();
        tenant.HasTenant.ShouldBeTrue();
        tenant.TenantId.ShouldBe(tenantId);
    }

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));
}