using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.API;
using FlowOS.Application.Commands;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using System.Collections.Generic;

namespace FlowOS.UnitTests.Integration;

public class DenyAllPolicyProvider : IPolicyProvider
{
    public Task<IEnumerable<Policy>> GetApplicablePoliciesAsync(PolicyContext context)
    {
        return Task.FromResult<IEnumerable<Policy>>(new List<Policy>
        {
            new Policy { Name = "DenyAll", Description = "Deny all requests" }
        });
    }

    public Task<IEnumerable<Policy>> GetAllPoliciesAsync()
    {
        return Task.FromResult<IEnumerable<Policy>>(new List<Policy>
        {
            new Policy { Name = "DenyAll", Description = "Deny all requests" }
        });
    }
}

public class PolicyEnforcementTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PolicyEnforcementTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Replace the default PolicyProvider with one that denies everything
                services.RemoveAll(typeof(IPolicyProvider));
                services.AddScoped<IPolicyProvider, DenyAllPolicyProvider>();
                
                // Replace DbContext to avoid conflicts (reuse same pattern as EndToEndTests)
                 services.RemoveAll(typeof(DbContextOptions));
                 services.RemoveAll(typeof(DbContextOptions<FlowOSDbContext>));
                 services.RemoveAll(typeof(FlowOSDbContext));
                 
                 services.AddScoped<FlowOSDbContext>(provider => 
                 {
                     var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                         .UseInMemoryDatabase("FlowOS_Policy_Db_" + Guid.NewGuid())
                         .EnableSensitiveDataLogging()
                         .Options;
                     
                     return new FlowOSDbContext(options);
                 });
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task StartWorkflow_ShouldBeDenied_WhenPolicyDenies()
    {
        // Arrange
        var command = new StartWorkflowCommand(
            TenantId: Guid.NewGuid(),
            WorkflowDefinitionId: Guid.NewGuid(),
            Version: 1,
            InitialStepId: "Start"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/workflows/start", command);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        
        // Optionally check content for "Policy 'DenyAll' denied execution" if 500 returns stack trace (dev mode)
    }
}
