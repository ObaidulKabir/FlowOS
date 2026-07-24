using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Api.Controllers;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using FlowOS.UnitTests.Workflows;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.UnitTests.Integration;

public class PoliciesControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public PoliciesControllerTests(CustomWebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_PoliciesApi_" + Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);

                services.AddScoped<FlowOSDbContext>(provider =>
                {
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(dbName)
                        .EnableSensitiveDataLogging()
                        .Options;

                    return new TestFlowOSDbContext(options);
                });
            });
        });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
    }

    [Fact]
    public async Task CreatePolicy_ShouldReturnCreated_WithPolicyId()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest
        {
            Name = "WeekendFreeze",
            ConditionJson = "{ \"frozenDays\": [\"Saturday\", \"Sunday\"] }"
        });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatePolicyResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.Id);

        // Verify persisted with correct tenant scope
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var saved = await db.Policies.FirstOrDefaultAsync(p => p.Id == body.Id);
        Assert.NotNull(saved);
        Assert.Equal(_tenantId, saved!.TenantId);
        Assert.Equal("WeekendFreeze", saved.Name);
    }

    [Fact]
    public async Task CreatePolicy_WhenDuplicateNameForSameTenant_ShouldReturnConflict()
    {
        // Arrange - create the first policy
        var first = await _client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest
        {
            Name = "UniqueNamePolicy",
            ConditionJson = "{}"
        });
        first.EnsureSuccessStatusCode();

        // Act - attempt to create a second policy with the same name for the same tenant
        var second = await _client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest
        {
            Name = "UniqueNamePolicy",
            ConditionJson = "{}"
        });

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreatePolicy_WithSameName_DifferentTenant_ShouldBothSucceed()
    {
        // Arrange - create policy for tenant A (default client tenant)
        var firstResponse = await _client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest
        {
            Name = "SharedName",
            ConditionJson = "{}"
        });
        firstResponse.EnsureSuccessStatusCode();

        // Act - create a policy with the same name under a different tenant
        using var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("x-tenant-id", Guid.NewGuid().ToString());
        otherClient.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");

        var secondResponse = await otherClient.PostAsJsonAsync("/api/policies", new CreatePolicyRequest
        {
            Name = "SharedName",
            ConditionJson = "{}"
        });

        // Assert - tenant isolation means both should succeed independently
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    [Fact]
    public async Task GetPolicy_ShouldReturnPolicy_WhenExists()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest
        {
            Name = "GetTestPolicy",
            ConditionJson = "{}"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatePolicyResponse>();

        // Act
        var response = await _client.GetAsync($"/api/policies/{created!.Id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var policy = await response.Content.ReadFromJsonAsync<Policy>();
        Assert.NotNull(policy);
        Assert.Equal("GetTestPolicy", policy!.Name);
    }

    [Fact]
    public async Task GetPolicy_ShouldReturnNotFound_WhenMissing()
    {
        // Act
        var response = await _client.GetAsync($"/api/policies/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPolicy_ShouldReturnNotFound_WhenPolicyBelongsToDifferentTenant()
    {
        // Arrange - create a policy for the default client's tenant
        var createResponse = await _client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest
        {
            Name = "TenantIsolatedPolicy",
            ConditionJson = "{}"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatePolicyResponse>();

        // Act - try to fetch it using a different tenant's context
        using var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("x-tenant-id", Guid.NewGuid().ToString());
        otherClient.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
        var response = await otherClient.GetAsync($"/api/policies/{created!.Id}");

        // Assert - cross-tenant access must not leak the policy
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record CreatePolicyResponse(Guid Id);
}
