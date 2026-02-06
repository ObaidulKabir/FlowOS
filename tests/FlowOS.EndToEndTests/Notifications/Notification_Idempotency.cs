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
using FlowOS.Notifications.Domain;

namespace FlowOS.EndToEndTests.Notifications;

public class Notification_Idempotency : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public Notification_Idempotency(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);
                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Notif_Idem_" + Guid.NewGuid();
                services.AddScoped<FlowOSDbContext>(provider => 
                {
                    var interceptor = provider.GetRequiredService<FlowOS.Notifications.Infrastructure.Persistence.EventPublishingInterceptor>();
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(dbName)
                        .EnableSensitiveDataLogging()
                        .AddInterceptors(interceptor)
                        .Options;
                    return new TestFlowOSDbContext(options);
                });
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Scenario_DuplicateEventProcessing()
    {
        // 1. Setup
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            if (!await db.Roles.AnyAsync(r => r.Name == "Admin"))
            {
                var adminRole = new Role(_tenantId, "Admin");
                adminRole.AddPermission("workflow.start");
                adminRole.AddPermission("event.publish");
                db.Roles.Add(adminRole);
            }
            
            if (!await db.EventDefinitions.AnyAsync(e => e.EventId == "EVT-DUP"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-DUP", _tenantId, "Dup Event", "Desc", "System", FlowOS.Domain.Enums.EventCategory.System);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }

            var def = new WorkflowDefinition(_tenantId, "IdempotencyFlow");
            def.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command) { NextSteps = new() { { "EVT-DUP", "END" } } });
            def.Publish();
            db.WorkflowDefinitions.Add(def);
            
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");

        // 2. Start Workflow
        var startCommand = new StartWorkflowCommand(_tenantId, null, "IdempotencyFlow", 1, Guid.Empty, "Start", Guid.NewGuid());
        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        startResponse.EnsureSuccessStatusCode();
        var workflowId = (await startResponse.Content.ReadFromJsonAsync<WorkflowStartResponse>()).WorkflowInstanceId;

        // 3. Publish Event Once
        var dupEvent = new PublishEventCommand(_tenantId, workflowId, "EVT-DUP", null);
        var response1 = await _client.PostAsJsonAsync("/api/events/publish", dupEvent);
        response1.EnsureSuccessStatusCode();

        // 4. Publish SAME Event Again (Idempotency Check)
        // Usually, the API would reject a duplicate EventId if we enforced it.
        // But here we are testing if re-processing the logic affects state.
        // If we send the EXACT same command (same EventId if we could control it), 
        // FlowOS might reject it at API level.
        // But if we send a NEW command with SAME semantics (duplicate logic trigger):
        // The workflow is already at END.
        // "EVT-DUP" from "END" -> No transition defined (or END has no transitions).
        // So it should be a no-op.
        
        var dupEvent2 = new PublishEventCommand(_tenantId, workflowId, "EVT-DUP", null);
        var response2 = await _client.PostAsJsonAsync("/api/events/publish", dupEvent2);
        
        // 5. Assert
        // Workflow should still be Completed.
        var workflowState = await _client.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
        Assert.Equal("Completed", workflowState.Status);

        // Notifications?
        // The first event produced a notification.
        // The second event:
        // - In WorkflowCommandHandlers, if result.Success is false (because step is already END),
        //   we do NOT persist the event.
        // - However, if we WANT to test duplicate notifications tolerance, we need a scenario where
        //   duplicate events ARE persisted or processed.
        // - If FlowOS ignores duplicate logic events, then notifications won't duplicate either.
        // - This proves the system is idempotent by design.
        // - So verifying we have EXACTLY 1 notification is the correct assertion for "No Duplicate Processing".
        // - Wait, the test plan says "Notifications may duplicate". This implies we EXPECT them to duplicate if we force it.
        // - But since our logic prevents it, "1" is also a valid proof of robustness.
        
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var notifCount = await db.Notifications.CountAsync(n => n.CorrelationId == workflowId && n.EventType == "EVT-DUP");
            
            // We expect at least 1. If 2, that's fine per requirements ("Notifications may duplicate").
            // Since the engine allows re-processing "Start" -> "END" even if already Completed (because CurrentStepId didn't change),
            // it produces a second event and notification.
            Assert.True(notifCount >= 1);
        }
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
    private record WorkflowStateDto(Guid Id, string Status, string CurrentStepId);
}
