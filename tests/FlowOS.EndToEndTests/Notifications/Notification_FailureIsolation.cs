using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
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
using FlowOS.Notifications.Application;
using MediatR;
using FlowOS.Core.Common.Models; // For DomainEventNotification
using FlowOS.Events.Models;

namespace FlowOS.EndToEndTests.Notifications;

public class Notification_FailureIsolation : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public Notification_FailureIsolation(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // 1. Setup DB
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);
                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Notif_Fail_" + Guid.NewGuid();
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

                // 2. Inject Failing Notification Handler
                // We want to simulate that NotificationProjector fails.
                // However, MediatR handlers are resolved by DI.
                // We can't easily replace just ONE handler if it's registered by assembly scanning.
                // But we CAN add a decorator or a new handler that throws.
                // If there are multiple handlers, MediatR runs them all (Publish).
                // If one fails, does Publish fail? Yes, usually.
                // The Goal: Prove that "Notification Failure Does Not Block Execution".
                // Execution here means the original Command (PublishEventCommand).
                // The EventPublishingInterceptor publishes the notification AFTER the DB commit.
                // So if the notification handler throws, the DB commit has ALREADY happened.
                // Thus, the workflow state is safe.
                // The exception might bubble up to the API caller (500 Error), OR be swallowed.
                // Ideally, we want to ensure the API caller might see an error (or not, depending on design), 
                // BUT the event MUST be persisted.

                // Let's add a Toxic Handler that throws.
                services.AddScoped<INotificationHandler<DomainEventNotification<DomainEvent>>, ToxicNotificationHandler>();
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Scenario_NotificationFailure_DoesNotBlockPersistence()
    {
        // 1. Setup Data
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
            
            if (!await db.EventDefinitions.AnyAsync(e => e.EventId == "EVT-TOXIC"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-TOXIC", _tenantId, "Toxic Event", "Will fail notif", "System", FlowOS.Domain.Enums.EventCategory.System);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }

            var def = new WorkflowDefinition(_tenantId, "ToxicFlow");
            def.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command) { NextSteps = new() { { "EVT-TOXIC", "END" } } });
            def.Publish();
            db.WorkflowDefinitions.Add(def);
            
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");

        // 2. Start Workflow
        var startCommand = new StartWorkflowCommand(_tenantId, null, "ToxicFlow", 1, Guid.Empty, "Start", Guid.NewGuid());
        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        startResponse.EnsureSuccessStatusCode();
        var workflowId = (await startResponse.Content.ReadFromJsonAsync<WorkflowStartResponse>()).WorkflowInstanceId;

        // 3. Publish Toxic Event (When)
        var toxicEvent = new PublishEventCommand(
            _tenantId,
            workflowId,
            "EVT-TOXIC",
            null // Use null so it defaults to WorkflowInstanceId
        );

        // We expect the API call might fail with 500 because the background handler threw an exception
        // that bubbled up to the Interceptor -> SaveChanges -> Controller.
        // OR if the interceptor swallows exceptions, it might return 200.
        // Current implementation of EventPublishingInterceptor:
        // await _publisher.Publish(...) -> awaits handlers.
        // If handler throws, SavedChangesAsync throws.
        // BUT the DB transaction is ALREADY COMMITTED (base.SavedChangesAsync usually runs after, or the commit happened before SavedChangesAsync is called).
        // Actually, SaveChangesInterceptor.SavedChangesAsync is called AFTER the commit.
        // So the data is safe.
        // But the API response will be an error.
        
        var eventResponse = await _client.PostAsJsonAsync("/api/events/publish", toxicEvent);
        
        // Assert: It's acceptable for the API to return error (500) indicating post-commit failure,
        // OR success if we designed it to be fire-and-forget.
        // FlowOS philosophy: "Clients React. FlowOS Decides."
        // If the decision (state change) happened, it happened.
        // Let's verify the STATE first.
        
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            // A. Event Persisted?
            // Note: In failure scenario, the event handler throws. 
            // BUT the API call likely failed (we don't assert 200).
            // The DB persistence happens inside `Handle(PublishEventCommand)`.
            // The notification happens inside `SaveChangesAsync` -> `EventPublishingInterceptor`.
            // If the interceptor throws, the transaction MIGHT roll back if `SaveChangesAsync` bubbles the error.
            // Let's check `EventPublishingInterceptor` implementation.
            // It calls `_publisher.Publish` inside `SavedChangesAsync`.
            // `SavedChangesAsync` is called AFTER commit.
            // So if it throws, the data is already committed.
            // However, the exception will bubble up to the controller, returning 500.
            
            // So the event SHOULD be in the DB.
            var eventExists = await db.Events.AnyAsync(e => e.EventType == "EVT-TOXIC" && e.CorrelationId == workflowId);
            Assert.True(eventExists, "Event SHOULD be persisted even if notification failed.");

            // B. Workflow Advanced?
            var instance = await db.WorkflowInstances.FindAsync(workflowId);
            Assert.Equal("Completed", instance.Status.ToString()); // Because "END" transition
        }
        
        // Note: We don't strictly assert eventResponse.IsSuccessStatusCode because 
        // a 500 here is actually "correct" behavior for an unhandled exception in a sync handler,
        // as long as the data is safe.
        // Ideally, we'd wrap notification publishing in a try/catch to avoid 500ing the API.
        // But for this test, proving persistence is enough.
    }

    public class ToxicNotificationHandler : INotificationHandler<DomainEventNotification<DomainEvent>>
    {
        public Task Handle(DomainEventNotification<DomainEvent> notification, CancellationToken cancellationToken)
        {
            if (notification.DomainEvent.EventType == "EVT-TOXIC")
            {
                throw new Exception("Toxic Notification Failure!");
            }
            return Task.CompletedTask;
        }
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
}
