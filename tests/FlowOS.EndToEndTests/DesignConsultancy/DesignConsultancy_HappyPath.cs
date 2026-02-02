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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.DesignConsultancy;

public class DesignConsultancy_HappyPath : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public DesignConsultancy_HappyPath(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_HappyPath_" + Guid.NewGuid();

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
    public async Task Scenario_DesignConsultancy_HappyPath()
    {
        // 1. Setup (Given)
        // Seed Tenant, Role, and Workflow
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            // Ensure Tenant (Might be seeded by Program, but let's be safe)
            // Actually Program seeds DefaultTenantId which matches ours.
            
            // Seed Admin Role for Client/Manager
            if (!await db.Roles.AnyAsync(r => r.Name == "Admin"))
            {
                var adminRole = new Role(_tenantId, "Admin");
                adminRole.AddPermission("workflow.start");
                adminRole.AddPermission("workflow.read");
                adminRole.AddPermission("task.complete");
                adminRole.AddPermission("event.publish");
                adminRole.AddPermission("agent.insight.publish");
                db.Roles.Add(adminRole);
            }

            // Workflow Definition is loaded from flowos-config/workflows/DesignConsultancy.json by ConfigurationLoader at startup.
            // We do not need to seed it manually here to avoid duplicates.

            // Seed Events
            if (!await db.EventDefinitions.AnyAsync(e => e.EventId == "EVT-DESIGN-APPROVED"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition(
                    "EVT-DESIGN-APPROVED", 
                    _tenantId, 
                    "Design Approved", 
                    "User approved design", 
                    "Design",
                    FlowOS.Domain.Enums.EventCategory.Decision,
                    1
                );
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }
            if (!await db.EventDefinitions.AnyAsync(e => e.EventId == "EVT-DESIGN-REJECTED"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition(
                    "EVT-DESIGN-REJECTED", 
                    _tenantId, 
                    "Design Rejected", 
                    "User rejected design", 
                    "Design",
                    FlowOS.Domain.Enums.EventCategory.Decision,
                    1
                );
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }
            
            await db.SaveChangesAsync();
        }

        // Setup Headers for Admin
        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");

        // 2. Client Starts Workflow (When)
        var startCommand = new StartWorkflowCommand(
            _tenantId, 
            null, 
            "DesignConsultancy", 
            1, 
            "Start", 
            Guid.NewGuid()
        );
        
        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        startResponse.EnsureSuccessStatusCode();
        var startResult = await startResponse.Content.ReadFromJsonAsync<WorkflowStartResponse>();
        var workflowId = startResult.WorkflowInstanceId;

        // Assert: Workflow Started and auto-advanced to DesignTask
        var workflowState = await GetWorkflowState(workflowId);
        Assert.Equal("DesignTask", workflowState.CurrentStepId);

        // 3. Designer Completes Task (When)
        // Note: The API treats the route ID as both WorkflowInstanceId and TaskId for now.
        var taskResponse = await _client.PostAsync($"/api/tasks/{workflowId}/complete", null);
        taskResponse.EnsureSuccessStatusCode();

        // Assert: Workflow advanced to Review
        workflowState = await GetWorkflowState(workflowId);
        Assert.Equal("Review", workflowState.CurrentStepId);

        // 4. Manager Approves via Event (When)
        var approveEvent = new PublishEventCommand(
            _tenantId,
            workflowId,
            "EVT-DESIGN-APPROVED",
            Guid.NewGuid()
        );

        var eventResponse = await _client.PostAsJsonAsync("/api/events/publish", approveEvent);
        eventResponse.EnsureSuccessStatusCode();

        // 5. Verify Final State (Then)
        workflowState = await GetWorkflowState(workflowId);
        Assert.Equal("Completed", workflowState.Status);
        // CurrentStepId should be whatever the last step was before END, or "END" depending on implementation.
        // WorkflowEngine.AdvanceTo("END") isn't called, it calls instance.Complete().
        // So CurrentStepId might remain "Review" but status is Completed.
        // Let's check Assert.Equal("Review", workflowState.CurrentStepId); OR just check status.
        // Actually, if nextStepId is END, it calls instance.Complete(). It does NOT update CurrentStepId to "END".
        // So it stays at "Review".
        Assert.Equal("Review", workflowState.CurrentStepId); 
    }

    private async Task<WorkflowStateDto> GetWorkflowState(Guid id)
    {
        var response = await _client.GetAsync($"/api/workflows/{id}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkflowStateDto>();
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
    private record WorkflowStateDto(Guid Id, string Status, string CurrentStepId);
}
