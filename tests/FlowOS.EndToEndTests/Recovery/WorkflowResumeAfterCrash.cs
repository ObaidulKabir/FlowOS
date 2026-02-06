using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using FlowOS.Security.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Recovery;

public class WorkflowResumeAfterCrash : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _sharedDbName = "FlowOS_E2E_Crash_" + Guid.NewGuid();

    public WorkflowResumeAfterCrash(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithSharedDb()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
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
                        .UseInMemoryDatabase(_sharedDbName) // Use SHARED DB Name
                        .EnableSensitiveDataLogging()
                        .Options;
                    
                    return new TestFlowOSDbContext(options);
                });
            });
        });
        
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("x-tenant-id");
        client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        client.DefaultRequestHeaders.Remove("X-Mock-Role");
        client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
        
        return client;
    }

    [Fact]
    public async Task Scenario_WorkflowResumeAfterCrash()
    {
        Guid workflowId;

        // 1. Session A: Start Workflow
        using (var clientA = CreateClientWithSharedDb())
        {
            // Seed Role (Only need to do this once per DB lifetime)
            // But we don't have easy access to scope here without creating one from factory.
            // Let's assume we can seed via API or just ensure DB is seeded.
            // Since we can't easily access the scope of `clientA`'s factory instance from here cleanly
            // (CreateClient creates a client, but we need the services).
            // We can do it inside the `WithWebHostBuilder` above but that runs per client creation.
            // Actually, `WithWebHostBuilder` creates a NEW factory derived from original.
            // We can create a scope from THAT derived factory.
            
            // Simplified: Just use `CreateClientWithSharedDb` which uses a factory.
            // We need to seed using a factory that points to the shared DB.
            // We can use `_factory.WithWebHostBuilder...` to create a temporary factory for seeding.
            
            // Seeding Logic
            var seedFactory = _factory.WithWebHostBuilder(builder => {
                 builder.ConfigureTestServices(services => {
                    var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                    foreach (var d in contextDescriptors) services.Remove(d);
                    var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                    foreach (var d in optionsDescriptors) services.Remove(d);
                    services.AddScoped<FlowOSDbContext>(provider => {
                        var options = new DbContextOptionsBuilder<FlowOSDbContext>().UseInMemoryDatabase(_sharedDbName).Options;
                        return new TestFlowOSDbContext(options);
                    });
                 });
            });
            using (var scope = seedFactory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
                if (!await db.Roles.AnyAsync(r => r.Name == "Admin"))
                {
                    var adminRole = new Role(_tenantId, "Admin");
                    adminRole.AddPermission("workflow.start");
                    adminRole.AddPermission("task.complete");
                    db.Roles.Add(adminRole);
                    await db.SaveChangesAsync();
                }
            }

            // Start Workflow
            var startCommand = new StartWorkflowCommand(_tenantId, null, "DesignConsultancy", 1, Guid.Empty, "Start", Guid.NewGuid());
            var response = await clientA.PostAsJsonAsync("/api/workflows/start", startCommand);
            response.EnsureSuccessStatusCode();
            workflowId = (await response.Content.ReadFromJsonAsync<WorkflowStartResponse>()).WorkflowInstanceId;
            
            // Verify Started
            var state = await clientA.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
            Assert.Equal("DesignTask", state.CurrentStepId);
        }

        // 2. Simulate Crash (ClientA disposed, FactoryA disposed effectively)
        // Now create Client B connected to SAME Persistent Store (InMemory DB with same name).

        using (var clientB = CreateClientWithSharedDb())
        {
            // 3. Resume (When)
            // Check state
            var state = await clientB.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
            Assert.Equal("DesignTask", state.CurrentStepId); // State preserved

            // Complete Task
            var taskResponse = await clientB.PostAsync($"/api/tasks/{workflowId}/complete", null);
            taskResponse.EnsureSuccessStatusCode();

            // Verify Advanced
            state = await clientB.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
            Assert.Equal("Review", state.CurrentStepId);
        }
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
    private record WorkflowStateDto(Guid Id, string Status, string CurrentStepId);
}
