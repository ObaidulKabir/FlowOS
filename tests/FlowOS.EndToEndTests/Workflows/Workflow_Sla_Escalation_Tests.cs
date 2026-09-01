using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Notifications.Domain;
using FlowOS.Security.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Workflows;

public class Workflow_Sla_Escalation_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    public Workflow_Sla_Escalation_Tests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Sla_" + Guid.NewGuid();

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
    public async Task Scenario_TaskSlaBreach_Automatically_Escalates_To_Higher_Step_And_Notifies()
    {
        // 1. Setup Definitions
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            if (!await db.Roles.AnyAsync(r => r.TenantId == _tenantId && r.Name == "Admin"))
            {
                var adminRole = new Role(_tenantId, "Admin");
                adminRole.AddPermission("workflow.start");
                adminRole.AddPermission("event.publish");
                db.Roles.Add(adminRole);
            }

            if (!await db.EventDefinitions.AnyAsync(e => e.TenantId == _tenantId && e.EventId == "EVT-SUBMIT"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-SUBMIT", _tenantId, "Submit", "Desc", "System", FlowOS.Domain.Enums.EventCategory.System);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }

            if (!await db.EventDefinitions.AnyAsync(e => e.TenantId == _tenantId && e.EventId == "EVT-ESCALATE"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-ESCALATE", _tenantId, "Escalate", "Desc", "System", FlowOS.Domain.Enums.EventCategory.System);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }

            var def = new WorkflowDefinition(_tenantId, "EscalationWorkflow");
            def.AddStep(new WorkflowStepDefinition("Draft", WorkflowStepType.Command) 
            { 
                NextSteps = new() { { "EVT-SUBMIT", "ManagerApproval" } } 
            });

            var managerStep = new WorkflowStepDefinition("ManagerApproval", WorkflowStepType.HumanTask)
            {
                AllowedRoles = new() { "Manager" },
                Sla = new StepSlaDefinition("1s", "EVT-ESCALATE", "DirectorApproval", "Director"),
                NextSteps = new() 
                { 
                    { "EVT-MANAGER-APPROVE", "END" },
                    { "EVT-ESCALATE", "DirectorApproval" }
                }
            };
            def.AddStep(managerStep);

            var directorStep = new WorkflowStepDefinition("DirectorApproval", WorkflowStepType.HumanTask)
            {
                AllowedRoles = new() { "Director" },
                NextSteps = new() { { "EVT-DIRECTOR-APPROVE", "END" } }
            };
            def.AddStep(directorStep);

            def.Publish();
            db.WorkflowDefinitions.Add(def);
            
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");

        // 2. Start Workflow
        var startCommand = new StartWorkflowCommand(_tenantId, null, "EscalationWorkflow", 1, Guid.Empty, "Draft", Guid.NewGuid());
        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        startResponse.EnsureSuccessStatusCode();
        var workflowId = (await startResponse.Content.ReadFromJsonAsync<WorkflowStartResponse>())!.WorkflowInstanceId;

        // 3. Transition from Draft -> ManagerApproval
        var submitEvent = new PublishEventCommand(_tenantId, workflowId, "EVT-SUBMIT", null);
        var submitResponse = await _client.PostAsJsonAsync("/api/events/publish", submitEvent);
        submitResponse.EnsureSuccessStatusCode();

        // 4. Verify workflow is at ManagerApproval in Waiting state
        var managerState = await _client.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
        Assert.NotNull(managerState);
        Assert.Equal("Waiting", managerState!.Status);
        Assert.Equal("ManagerApproval", managerState.CurrentStepId);

        // 5. Verify SLA Boundary Timer is scheduled in DB
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var timerJob = await db.WorkflowTimerJobs.FirstOrDefaultAsync(t => t.WorkflowInstanceId == workflowId && t.StepId == "ManagerApproval");
            Assert.NotNull(timerJob);
            Assert.Equal("EVT-ESCALATE", timerJob!.TriggerEventType);
            Assert.False(timerJob.IsProcessed);
        }

        // 6. Wait for timer processor to trigger EVT-ESCALATE automatically after 1 second SLA
        var maxWaitMs = 6000;
        var elapsedMs = 0;
        var escalated = false;

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
            if (state?.CurrentStepId == "DirectorApproval")
            {
                escalated = true;
                break;
            }
        }

        Assert.True(escalated, "Workflow should have automatically escalated to DirectorApproval after SLA timer fired.");

        // 7. Verify High-Severity Escalation Notification was created
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var notif = await db.Notifications.FirstOrDefaultAsync(n => n.TenantId == _tenantId && n.EventType == "EVT-ESCALATE");
            Assert.NotNull(notif);
            Assert.Equal("High", notif!.Severity);
            Assert.Contains("escalated", notif.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Scenario_EarlyTaskCompletion_Cancels_Sla_Boundary_Timer()
    {
        // 1. Setup Definitions
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            if (!await db.Roles.AnyAsync(r => r.TenantId == _tenantId && r.Name == "Admin"))
            {
                var adminRole = new Role(_tenantId, "Admin");
                adminRole.AddPermission("workflow.start");
                adminRole.AddPermission("event.publish");
                db.Roles.Add(adminRole);
            }

            if (!await db.EventDefinitions.AnyAsync(e => e.TenantId == _tenantId && e.EventId == "EVT-MANAGER-APPROVE"))
            {
                var evt = new FlowOS.Domain.Entities.EventDefinition("EVT-MANAGER-APPROVE", _tenantId, "Approve", "Desc", "System", FlowOS.Domain.Enums.EventCategory.System);
                evt.Publish();
                db.EventDefinitions.Add(evt);
            }

            var def = new WorkflowDefinition(_tenantId, "EarlyApprovalFlow");
            var taskStep = new WorkflowStepDefinition("ManagerReview", WorkflowStepType.HumanTask)
            {
                Sla = new StepSlaDefinition("30s", "EVT-ESCALATE"), // 30 second SLA
                NextSteps = new() 
                { 
                    { "EVT-MANAGER-APPROVE", "END" },
                    { "EVT-ESCALATE", "END" }
                }
            };
            def.AddStep(taskStep);
            def.Publish();
            db.WorkflowDefinitions.Add(def);
            
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");

        // 2. Start Workflow directly at ManagerReview
        var startCommand = new StartWorkflowCommand(_tenantId, null, "EarlyApprovalFlow", 1, Guid.Empty, "ManagerReview", Guid.NewGuid());
        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        startResponse.EnsureSuccessStatusCode();
        var workflowId = (await startResponse.Content.ReadFromJsonAsync<WorkflowStartResponse>())!.WorkflowInstanceId;

        // 3. Verify SLA Boundary Timer was scheduled
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var timerJob = await db.WorkflowTimerJobs.FirstOrDefaultAsync(t => t.WorkflowInstanceId == workflowId && t.StepId == "ManagerReview");
            Assert.NotNull(timerJob);
            Assert.False(timerJob!.IsProcessed);
        }

        // 4. Manager completes the task early before the 30s SLA expires
        var approveEvent = new PublishEventCommand(_tenantId, workflowId, "EVT-MANAGER-APPROVE", null);
        var approveResponse = await _client.PostAsJsonAsync("/api/events/publish", approveEvent);
        approveResponse.EnsureSuccessStatusCode();

        // 5. Verify workflow is Completed
        var finalState = await _client.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
        Assert.NotNull(finalState);
        Assert.Equal("Completed", finalState!.Status);

        // 6. Verify that the scheduled SLA timer was cleanly cancelled (marked IsProcessed = true)
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var timerJob = await db.WorkflowTimerJobs.FirstOrDefaultAsync(t => t.WorkflowInstanceId == workflowId && t.StepId == "ManagerReview");
            Assert.NotNull(timerJob);
            Assert.True(timerJob!.IsProcessed, "Boundary timer should have been marked as processed/cancelled upon early step exit.");
        }
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
    private record WorkflowStateDto(Guid Id, string Status, string CurrentStepId);
}
