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

namespace FlowOS.EndToEndTests.DesignConsultancy;

public class DesignConsultancy_Rejection : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public DesignConsultancy_Rejection(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Rejection_" + Guid.NewGuid();

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
    public async Task Scenario_DesignConsultancy_Rejection()
    {
        // 1. Setup (Given)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            // Seed Admin Role
            if (!await db.Roles.AnyAsync(r => r.Name == "Admin"))
            {
                var adminRole = new Role(_tenantId, "Admin");
                adminRole.AddPermission("workflow.start");
                adminRole.AddPermission("workflow.read");
                adminRole.AddPermission("task.complete");
                adminRole.AddPermission("event.publish");
                db.Roles.Add(adminRole);
            }

            // Seed Events
            if (!await db.EventDefinitions.AnyAsync(e => e.EventId == "EVT-DESIGN-APPROVED"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-DESIGN-APPROVED", _tenantId, "Approve", "Desc", "Design", FlowOS.Domain.Enums.EventCategory.Decision);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }
            if (!await db.EventDefinitions.AnyAsync(e => e.EventId == "EVT-DESIGN-REJECTED"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-DESIGN-REJECTED", _tenantId, "Reject", "Desc", "Design", FlowOS.Domain.Enums.EventCategory.Decision);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }
            
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");

        // 2. Start Workflow
        var startCommand = new StartWorkflowCommand(_tenantId, null, "DesignConsultancy", 1, "Start", Guid.NewGuid());
        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        startResponse.EnsureSuccessStatusCode();
        var workflowId = (await startResponse.Content.ReadFromJsonAsync<WorkflowStartResponse>()).WorkflowInstanceId;

        // 3. Complete Design Task
        await _client.PostAsync($"/api/tasks/{workflowId}/complete", null);

        // 4. Reject (When)
        var rejectEvent = new PublishEventCommand(_tenantId, workflowId, "EVT-DESIGN-REJECTED", Guid.NewGuid());
        var eventResponse = await _client.PostAsJsonAsync("/api/events/publish", rejectEvent);
        eventResponse.EnsureSuccessStatusCode();

        // 5. Assert (Then)
        var workflowState = await GetWorkflowState(workflowId);
        
        // Should be Completed (via Rejected -> Default -> END)
        Assert.Equal("Completed", workflowState.Status);
        // Note: CurrentStepId stays at "Rejected" because "END" transition just completes it.
        Assert.Equal("Rejected", workflowState.CurrentStepId);
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
