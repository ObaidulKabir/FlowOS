using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
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

namespace FlowOS.EndToEndTests.Workflows;

public class Workflow_Timer_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    private readonly Guid _tenantId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    public Workflow_Timer_Tests(WebApplicationFactory<Program> factory, Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Timer_" + Guid.NewGuid();

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
    public async Task Scenario_WorkflowWithTimerStep_Pauses_And_Resumes_When_Timer_Fires()
    {
        // 1. Setup Definitions
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            // Seed Admin Role
            if (!await db.Roles.AnyAsync(r => r.TenantId == _tenantId && r.Name == "Admin"))
            {
                var adminRole = new Role(_tenantId, "Admin");
                adminRole.AddPermission("workflow.start");
                adminRole.AddPermission("event.publish");
                db.Roles.Add(adminRole);
            }
            
            // Seed Event Definitions
            if (!await db.EventDefinitions.AnyAsync(e => e.TenantId == _tenantId && e.EventId == "EVT-START-TIMER"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-START-TIMER", _tenantId, "Start Timer Event", "Desc", "System", FlowOS.Domain.Enums.EventCategory.System);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }

            if (!await db.EventDefinitions.AnyAsync(e => e.TenantId == _tenantId && e.EventId == "EVT-TIMER-EXPIRED"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-TIMER-EXPIRED", _tenantId, "Timer Expired Event", "Desc", "System", FlowOS.Domain.Enums.EventCategory.System);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }

            // Seed Workflow Definition:
            // Step 1: Start (Command) -> on EVT-START-TIMER -> Step 2: AutoDelay (Timer)
            // Step 2: AutoDelay (Timer, duration 1s) -> on EVT-TIMER-EXPIRED -> END
            var def = new WorkflowDefinition(_tenantId, "TimerWorkflow");
            def.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command) 
            { 
                NextSteps = new() { { "EVT-START-TIMER", "AutoDelay" } } 
            });

            var timerStep = new WorkflowStepDefinition("AutoDelay", WorkflowStepType.Timer)
            {
                NextSteps = new() { { "EVT-TIMER-EXPIRED", "END" } },
                Conditions = new() { { "Duration", "1" } } // 1 second timer
            };
            def.AddStep(timerStep);
            def.Publish();
            db.WorkflowDefinitions.Add(def);
            
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");

        // 2. Start Workflow
        var startCommand = new StartWorkflowCommand(_tenantId, null, "TimerWorkflow", 1, Guid.Empty, "Start", Guid.NewGuid());
        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        startResponse.EnsureSuccessStatusCode();
        var workflowId = (await startResponse.Content.ReadFromJsonAsync<WorkflowStartResponse>())!.WorkflowInstanceId;

        // 3. Publish Event to transition from Start -> AutoDelay (Timer)
        var triggerEvent = new PublishEventCommand(
            _tenantId,
            workflowId,
            "EVT-START-TIMER",
            null
        );
        var eventResponse = await _client.PostAsJsonAsync("/api/events/publish", triggerEvent);
        eventResponse.EnsureSuccessStatusCode();

        // 4. Assert workflow entered Waiting state on AutoDelay step
        var workflowState = await _client.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
        Assert.NotNull(workflowState);
        Assert.Equal("Waiting", workflowState!.Status);
        Assert.Equal("AutoDelay", workflowState.CurrentStepId);

        // 5. Verify Timer Job is scheduled in the DB
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var timerJob = await db.WorkflowTimerJobs.FirstOrDefaultAsync(t => t.WorkflowInstanceId == workflowId && t.StepId == "AutoDelay");
            Assert.NotNull(timerJob);
            Assert.Equal("EVT-TIMER-EXPIRED", timerJob!.TriggerEventType);
            Assert.False(timerJob.IsProcessed);
        }

        // 6. Wait for the background timer processor to trigger (1 second timer + polling interval)
        var maxWaitMs = 6000;
        var elapsedMs = 0;
        var completed = false;

        while (elapsedMs < maxWaitMs)
        {
            await Task.Delay(500);
            elapsedMs += 500;

            using (var scope = _factory.Server.Services.CreateScope())
            {
                var timerService = scope.ServiceProvider.GetRequiredService<FlowOS.Application.Common.Interfaces.IWorkflowTimerService>();
                await timerService.ExecuteDueTimersAsync();
            }

            var state = await _client.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
            if (state?.Status == "Completed")
            {
                completed = true;
                break;
            }
        }

        Assert.True(completed, "Workflow should have automatically resumed and completed after timer expired.");
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
    private record WorkflowStateDto(Guid Id, string Status, string CurrentStepId);
}
