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

public class Notification_HappyPath : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public Notification_HappyPath(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Notification_Happy_" + Guid.NewGuid();

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
    public async Task Scenario_NotificationOnEventCommit()
    {
        // 1. Setup
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            // Seed Admin Role
            if (!await db.Roles.AnyAsync(r => r.Name == "Admin"))
            {
                var adminRole = new Role(_tenantId, "Admin");
                adminRole.AddPermission("workflow.start");
                adminRole.AddPermission("event.publish");
                db.Roles.Add(adminRole);
            }
            
            // Seed Event Definition
            if (!await db.EventDefinitions.AnyAsync(e => e.EventId == "EVT-TEST-NOTIF"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-TEST-NOTIF", _tenantId, "Test Notif", "Desc", "System", FlowOS.Domain.Enums.EventCategory.System);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }

            // Seed Workflow Definition (Minimal)
            var def = new WorkflowDefinition(_tenantId, "NotificationFlow");
            def.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command) { NextSteps = new() { { "EVT-TEST-NOTIF", "END" } } });
            def.Publish();
            db.WorkflowDefinitions.Add(def);
            
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");

        // 2. Start Workflow
        var startCommand = new StartWorkflowCommand(_tenantId, null, "NotificationFlow", 1, Guid.Empty, "Start", Guid.NewGuid());
        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        startResponse.EnsureSuccessStatusCode();
        var workflowId = (await startResponse.Content.ReadFromJsonAsync<WorkflowStartResponse>()).WorkflowInstanceId;

        // 3. Publish Approval Event (When)
        var approveEvent = new PublishEventCommand(
            _tenantId,
            workflowId,
            "EVT-TEST-NOTIF",
            null // Use null so it defaults to WorkflowInstanceId
        );

        var eventResponse = await _client.PostAsJsonAsync("/api/events/publish", approveEvent);
        eventResponse.EnsureSuccessStatusCode();

        // 4. Assert
        
        // A. Workflow Reaches End
        var workflowState = await _client.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
        Assert.Equal("Completed", workflowState.Status);

        // B. Event Exists in Timeline (via DB check or API)
        // Since API doesn't expose timeline events yet easily, let's check DB directly via scope
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var eventExists = await db.Events.AnyAsync(e => e.EventType == "EVT-TEST-NOTIF" && e.CorrelationId == workflowId);
            Assert.True(eventExists, "Event should exist in timeline");

            // C. Notification Record Exists
            var notification = await db.Notifications.FirstOrDefaultAsync(n => n.CorrelationId == workflowId && n.EventType == "EVT-TEST-NOTIF");
            Assert.NotNull(notification);
            
            // D. Correct mapping
            Assert.Equal("Event: EVT-TEST-NOTIF", notification.Message); // Default mapping
        }
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
    private record WorkflowStateDto(Guid Id, string Status, string CurrentStepId);
}
