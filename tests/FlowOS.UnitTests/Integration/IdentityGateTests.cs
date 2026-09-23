using System.Net;
using System.Net.Http.Headers;
using FlowOS.Core.Security;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Interfaces;
using FlowOS.UnitTests.Workflows;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.UnitTests.Integration;

public class IdentityGateTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly CustomWebApplicationFactory<Program> _factory;

    public IdentityGateTests(CustomWebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Development_MockAuth_StillAuthenticatesWithoutJwt()
    {
        var client = CreateInMemoryClient();
        client.DefaultRequestHeaders.Add("x-tenant-id", Guid.NewGuid().ToString());

        var response = await client.GetAsync("/api/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MockAuthDisabled_RejectsUnauthenticatedApiCalls()
    {
        var client = CreateInMemoryClient(allowMockAuth: false);
        client.DefaultRequestHeaders.Add("x-tenant-id", Guid.NewGuid().ToString());

        var response = await client.GetAsync("/api/tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MockAuthDisabled_Audit_AcceptsDemoApiKey()
    {
        var client = CreateInMemoryClient(allowMockAuth: false);
        client.DefaultRequestHeaders.Add("x-tenant-id", TenantIdentityRules.DemoTenantId.ToString());
        client.DefaultRequestHeaders.Add("X-API-Key", "flowos_prod_secret_key_32_chars_min");

        var response = await client.GetAsync($"/api/workflows/{Guid.NewGuid()}/audit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MockAuthDisabled_Audit_AcceptsJwtWhenApiKeyIsInvalid()
    {
        var tenantId = Guid.NewGuid();
        var factory = CreateInMemoryFactory(allowMockAuth: false);
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateToken(
            Guid.NewGuid(),
            "audit@flowos.test",
            "Audit User",
            tenantId,
            "Audit Tenant",
            "Tenant");

        client.DefaultRequestHeaders.Add("x-tenant-id", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-API-Key", "stale-or-unknown-key");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/workflows/{Guid.NewGuid()}/audit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateInMemoryClient(bool allowMockAuth = true)
        => CreateInMemoryFactory(allowMockAuth).CreateClient();

    private WebApplicationFactory<Program> CreateInMemoryFactory(bool allowMockAuth = true)
    {
        var dbName = "FlowOS_IdentityGate_" + Guid.NewGuid();
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FlowOS:Identity:AllowMockAuth"] = allowMockAuth ? "true" : "false",
                    ["UseInMemoryDatabase"] = "true",
                    ["ConnectionStrings:DefaultConnection"] = ""
                });
            });
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);

                services.AddScoped<FlowOSDbContext>(_ =>
                {
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(dbName)
                        .Options;
                    return new TestFlowOSDbContext(options);
                });
            });
        });
    }
}
