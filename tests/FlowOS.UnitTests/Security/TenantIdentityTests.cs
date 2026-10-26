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
    public void IsPlatformAdministrator_AllowsOnlyPlatformTenantPrivilegedRoles()
    {
        Assert.True(TenantIdentityRules.IsPlatformAdministrator(
            TenantIdentityRules.PlatformTenantId, new[] { "Admin" }));
        // The demo tenant is reachable with public demo keys and must never act across tenants.
        Assert.False(TenantIdentityRules.IsPlatformAdministrator(
            TenantIdentityRules.DemoTenantId, new[] { "SuperAdmin" }));
        Assert.False(TenantIdentityRules.IsPlatformAdministrator(
            TenantIdentityRules.DemoTenantId, new[] { "Admin" }));
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

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Production", null, false)]
    [InlineData("Staging", null, false)]
    [InlineData("Production", "true", true)]
    [InlineData("Development", "false", false)]
    public void DemoKeysAllowed_DefaultsToDevelopmentOnly(string environment, string? configured, bool expected)
    {
        Assert.Equal(expected, TenantIdentityRules.DemoKeysAllowed(environment, configured));
    }

    [Theory]
    [InlineData("flowos_prod_secret_key_32_chars_min")]
    [InlineData("local-development-key-change-me")]
    [InlineData("YOUR_PRODUCTION_API_KEY")]
    public void WellKnownDemoKeys_AreRejectedInProduction(string key)
    {
        Assert.False(TenantIdentityRules.IsAcceptedDemoApiKey(key, "Production", null));
        Assert.True(TenantIdentityRules.IsAcceptedDemoApiKey(key, "Development", null));
    }

    [Fact]
    public void OrdinaryKey_IsNeverADemoKey()
    {
        Assert.False(TenantIdentityRules.IsAcceptedDemoApiKey("a-random-tenant-key-0123456789", "Development", "true"));
    }
}
