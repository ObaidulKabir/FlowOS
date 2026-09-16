using System.Net;
using FlowOS.Infrastructure.Persistence;
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

    private HttpClient CreateInMemoryClient(bool allowMockAuth = true)
    {
        var dbName = "FlowOS_IdentityGate_" + Guid.NewGuid();
        var factory = _factory.WithWebHostBuilder(builder =>
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

        return factory.CreateClient();
    }
}
