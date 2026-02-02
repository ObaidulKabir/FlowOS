using System;
using System.Linq; // For SingleOrDefault
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

using FlowOS.Security.Models; // For Role
using FlowOS.UnitTests.Workflows; // For TestFlowOSDbContext

namespace FlowOS.UnitTests.Integration;

public class EndToEndTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public EndToEndTests(CustomWebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // ... (Keep existing DB setup)
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_" + Guid.NewGuid();

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
    }

    [Fact]
    public async Task StartWorkflow_ShouldReturnInstanceId()
    {
        Guid workflowId;
        int workflowVersion;

        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            // Seed Role
            var adminRole = new Role(_tenantId, "Admin");
            adminRole.AddPermission("workflow.start");
            db.Roles.Add(adminRole);

            // Seed Workflow
            var def = new WorkflowDefinition(_tenantId, "IntegrationTestFlow");
            var step = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
            step.NextSteps.Add("Default", "END");
            def.AddStep(step);
            def.Publish();
            db.WorkflowDefinitions.Add(def);
            
            await db.SaveChangesAsync();
            
            workflowId = def.Id;
            workflowVersion = def.Version;
        }

        // Setup Headers
        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");

        // Act
        var command = new StartWorkflowCommand(
            _tenantId, 
            workflowId, 
            null, 
            workflowVersion, 
            Guid.NewGuid(), // WorkflowClassId
            "Start", 
            Guid.NewGuid()
        );
        
        var response = await _client.PostAsJsonAsync("/api/workflows/start", command);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WorkflowStartResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.WorkflowInstanceId);
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
}
