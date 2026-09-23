using System.Security.Claims;
using FlowOS.API.Services;
using FlowOS.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FlowOS.UnitTests.Security;

public class TenantIdentityTests
{
    [Fact]
    public void AllowMockAuth_DefaultsToDevelopmentOnly()
    {
        Assert.True(TenantIdentityRules.AllowMockAuth("Development", null));
        Assert.False(TenantIdentityRules.AllowMockAuth("Staging", null));
        Assert.False(TenantIdentityRules.AllowMockAuth("Production", null));
        Assert.False(TenantIdentityRules.AllowMockAuth("Development", "false"));
        Assert.True(TenantIdentityRules.AllowMockAuth("Production", "true"));
    }

    [Fact]
    public void CurrentUser_UsesCredentialTenant_NotSpoofedHeader()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim("tenant_id", tenantA.ToString()),
                    new Claim(ClaimTypes.Role, "User")
                }, "Bearer"))
            }
        };
        accessor.HttpContext.Request.Headers["x-tenant-id"] = tenantB.ToString();

        var service = new CurrentUserService(
            accessor,
            new FakeHostEnvironment(Environments.Production));

        Assert.Equal(tenantA, service.TenantId);
        Assert.DoesNotContain("Admin", service.Roles);
    }

    [Fact]
    public void IsPlatformAdministrator_AllowsPlatformAndDemoPrivilegedRoles()
    {
        Assert.True(TenantIdentityRules.IsPlatformAdministrator(
            TenantIdentityRules.PlatformTenantId, new[] { "Admin" }));
        Assert.True(TenantIdentityRules.IsPlatformAdministrator(
            TenantIdentityRules.DemoTenantId, new[] { "SuperAdmin" }));
        Assert.False(TenantIdentityRules.IsPlatformAdministrator(
            Guid.NewGuid(), new[] { "Admin" }));
        Assert.False(TenantIdentityRules.IsPlatformAdministrator(
            TenantIdentityRules.PlatformTenantId, new[] { "ApiKey" }));
    }

    [Fact]
    public void ResolveApiKeyRole_MapsDemoAndWildcardKeysToAdmin()
    {
        Assert.Equal("Admin", TenantIdentityRules.ResolveApiKeyRole(Array.Empty<string>(), isDemoKey: true));
        Assert.Equal("Admin", TenantIdentityRules.ResolveApiKeyRole(new[] { "*" }, isDemoKey: false));
        Assert.Equal("Admin", TenantIdentityRules.ResolveApiKeyRole(new[] { "admin:*" }, isDemoKey: false));
        Assert.Equal("ApiKey", TenantIdentityRules.ResolveApiKeyRole(new[] { "mcp:read" }, isDemoKey: false));
        Assert.Equal("ApiKey", TenantIdentityRules.ResolveApiKeyRole(null, isDemoKey: false));
    }

    [Fact]
    public void CurrentUser_IgnoresHeaderTenant_WhenMockAuthIsOff()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Request.Headers["x-tenant-id"] = Guid.NewGuid().ToString();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TenantIdentityRules.AllowMockAuthKey] = "false"
            })
            .Build();

        var service = new CurrentUserService(
            accessor,
            new FakeHostEnvironment(Environments.Development),
            config);

        Assert.Equal(Guid.Empty, service.TenantId);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "FlowOS.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
