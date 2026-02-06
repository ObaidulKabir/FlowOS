using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Api.Controllers; // For request DTOs
using FlowOS.Security.Models;
using FluentAssertions;
using Xunit;

namespace FlowOS.E2E.Tests.ApiTests;

public class SecurityTests : IClassFixture<FlowOSWebApplicationFactory>
{
    private readonly FlowOSWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public SecurityTests(FlowOSWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
    }

    [Fact]
    public async Task Role_Lifecycle_ShouldWork()
    {
        // 1. Create Role
        var roleReq = new CreateRoleRequest { RoleName = "TestRole" };
        var createResp = await _client.PostAsJsonAsync("/api/roles", roleReq);
        createResp.EnsureSuccessStatusCode();
        
        var createResult = await createResp.Content.ReadFromJsonAsync<RoleDto>();
        createResult.Should().NotBeNull();
        createResult!.Id.Should().NotBeEmpty();
        var roleId = createResult.Id;

        // 2. Add Capability
        var capReq = new AddCapabilityRequest { CapabilityCode = "workflow.start" };
        var capResp = await _client.PostAsJsonAsync($"/api/roles/{roleId}/capabilities", capReq);
        capResp.EnsureSuccessStatusCode();

        // 3. Get Role and Verify
        var getResp = await _client.GetAsync($"/api/roles/{roleId}");
        getResp.EnsureSuccessStatusCode();
        var role = await getResp.Content.ReadFromJsonAsync<Role>();
        
        role.Should().NotBeNull();
        role!.Name.Should().Be("TestRole");
        role.Permissions.Should().Contain("workflow.start");
    }

    [Fact]
    public async Task Policy_Lifecycle_ShouldWork()
    {
        // 1. Create Policy
        var policyReq = new CreatePolicyRequest 
        { 
            Name = "NoWeekendWork", 
            ConditionJson = "{\"DayOfWeek\": \"Saturday\"}" 
        };
        
        var createResp = await _client.PostAsJsonAsync("/api/policies", policyReq);
        createResp.EnsureSuccessStatusCode();
        
        var createResult = await createResp.Content.ReadFromJsonAsync<PolicyDto>();
        createResult.Should().NotBeNull();
        createResult!.Id.Should().NotBeEmpty();
        var policyId = createResult.Id;

        // 2. Get Policy
        var getResp = await _client.GetAsync($"/api/policies/{policyId}");
        getResp.EnsureSuccessStatusCode();
        var policy = await getResp.Content.ReadFromJsonAsync<Policy>();
        
        policy.Should().NotBeNull();
        policy!.Name.Should().Be("NoWeekendWork");
        policy.ConditionJson.Should().Be("{\"DayOfWeek\": \"Saturday\"}");
    }

    // Helper DTOs for deserialization if Controller DTOs are not shared or accessible easily
    private class RoleDto { public Guid Id { get; set; } public string Name { get; set; } }
    private class PolicyDto { public Guid Id { get; set; } }
}
