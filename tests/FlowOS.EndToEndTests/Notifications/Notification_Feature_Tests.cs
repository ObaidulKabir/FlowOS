using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Notifications;

public class Notification_Feature_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _user1 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly Guid _user2 = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public Notification_Feature_Tests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Notif_Features_" + Guid.NewGuid();

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
    public async Task Scenario_TargetedNotifications_And_MarkAsRead_EndToEnd()
    {
        // 1. Setup Definitions
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
            if (!await db.EventDefinitions.AnyAsync(e => e.EventId == "EVT-RICH-TASK"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-RICH-TASK", _tenantId, "Rich Task Event", "Desc", "System", FlowOS.Domain.Enums.EventCategory.System);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }

            // Seed Workflow Definition
            var def = new WorkflowDefinition(_tenantId, "RichNotifFlow");
            def.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command) { NextSteps = new() { { "EVT-RICH-TASK", "END" } } });
            def.Publish();
            db.WorkflowDefinitions.Add(def);
            
            await db.SaveChangesAsync();
        }

        // Set Headers for Admin User 1
        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
        _client.DefaultRequestHeaders.Remove("X-Mock-UserId");
        _client.DefaultRequestHeaders.Add("X-Mock-UserId", _user1.ToString());

        // 2. Start Workflow
        var startCommand = new StartWorkflowCommand(_tenantId, null, "RichNotifFlow", 1, Guid.Empty, "Start", Guid.NewGuid());
        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        startResponse.EnsureSuccessStatusCode();
        var workflowId = (await startResponse.Content.ReadFromJsonAsync<WorkflowStartResponse>())!.WorkflowInstanceId;

        // 3. Publish Event targeted at User 1 with custom message and severity
        var richEvent = new PublishEventCommand(
            _tenantId,
            workflowId,
            "EVT-RICH-TASK",
            CorrelationId: null,
            Payload: new Dictionary<string, object>
            {
                ["Message"] = "Please review urgent expense report",
                ["Severity"] = "Critical",
                ["TargetUserId"] = _user1.ToString()
            }
        );

        var eventResponse = await _client.PostAsJsonAsync("/api/events/publish", richEvent);
        eventResponse.EnsureSuccessStatusCode();

        // 4. Query Notifications as User 1
        var user1NotifsResponse = await _client.GetAsync("/api/notifications");
        user1NotifsResponse.EnsureSuccessStatusCode();
        var user1Notifs = await user1NotifsResponse.Content.ReadFromJsonAsync<List<NotificationDto>>();

        Assert.NotNull(user1Notifs);
        var user1Notif = Assert.Single(user1Notifs);
        Assert.Equal("Please review urgent expense report", user1Notif.Message);
        Assert.Equal("Critical", user1Notif.Severity);
        Assert.Equal("EVT-RICH-TASK", user1Notif.EventType);
        Assert.False(user1Notif.IsRead);

        // 5. Query Notifications as User 2 (Should NOT see User 1's targeted notification)
        _client.DefaultRequestHeaders.Remove("X-Mock-UserId");
        _client.DefaultRequestHeaders.Add("X-Mock-UserId", _user2.ToString());

        var user2NotifsResponse = await _client.GetAsync("/api/notifications");
        user2NotifsResponse.EnsureSuccessStatusCode();
        var user2Notifs = await user2NotifsResponse.Content.ReadFromJsonAsync<List<NotificationDto>>();

        Assert.NotNull(user2Notifs);
        Assert.Empty(user2Notifs);

        // 6. Switch back to User 1 and Mark the notification as Read
        _client.DefaultRequestHeaders.Remove("X-Mock-UserId");
        _client.DefaultRequestHeaders.Add("X-Mock-UserId", _user1.ToString());

        var markReadResponse = await _client.PutAsync($"/api/notifications/{user1Notif.Id}/read", null);
        Assert.True(markReadResponse.IsSuccessStatusCode);

        // 7. Verify notification is now marked as Read
        var refreshedNotifsResponse = await _client.GetAsync("/api/notifications");
        var refreshedNotifs = await refreshedNotifsResponse.Content.ReadFromJsonAsync<List<NotificationDto>>();
        Assert.NotNull(refreshedNotifs);
        var refreshedNotif = Assert.Single(refreshedNotifs);
        Assert.True(refreshedNotif.IsRead);
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
    private record NotificationDto(Guid Id, string Message, string Severity, DateTime CreatedAt, string EventType, bool IsRead);
}
