using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.DTOs;
using FlowOS.Application.DTOs.Workflows;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using FlowOS.Security.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.E2E.Tests.ApiTests;

public class TasksTests : IClassFixture<FlowOSWebApplicationFactory>
{
    private readonly FlowOSWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public TasksTests(FlowOSWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
    }

    [Fact]
    public async Task HumanTask_Should_Wait_And_Complete()
    {
        Guid workflowClassId;

        // 1. Seed Data
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

            // Seed Admin Role
            var adminRole = new Role(_tenantId, "Admin");
            adminRole.AddPermission("workflow.start");
            adminRole.AddPermission("workflow.create");
            adminRole.AddPermission("workflow.read");
            adminRole.AddPermission("event.publish");
            adminRole.AddPermission("task.read");
            adminRole.AddPermission("task.complete");
            context.Roles.Add(adminRole);

            // Seed Events
            var evtStart = new EventDefinition(
                "EVT-START", _tenantId, "Start Event", "Starts process", "System", EventCategory.System);
            evtStart.Publish();
            context.EventDefinitions.Add(evtStart);

            var evtTaskCompleted = new EventDefinition(
                "TaskCompleted", _tenantId, "Task Completed", "System event for task completion", "System", EventCategory.System);
            evtTaskCompleted.Publish();
            context.EventDefinitions.Add(evtTaskCompleted);

            // Seed WorkflowClass (Governance)
            var bp = new WorkflowClassBlueprint
            {
                Events = new List<EventBlueprint> 
                { 
                    new EventBlueprint { EventId = "EVT-START", Name = "Start" },
                    new EventBlueprint { EventId = "TaskCompleted", Name = "Task Completed" }
                },
                StateMachine = new StateMachineBlueprint 
                { 
                    InitialState = "New", 
                    States = new List<string> { "New", "InReview", "Completed" } 
                },
                Workflow = new WorkflowBlueprint 
                { 
                    StartStepId = "Step1", 
                    Steps = new List<StepBlueprint> 
                    { 
                        new StepBlueprint 
                        { 
                            StepId = "Step1", 
                            StepType = "Command",
                            NextSteps = new Dictionary<string, string> { { "EVT-START", "UserTask" } }
                        },
                        new StepBlueprint 
                        { 
                            StepId = "UserTask", 
                            StepType = "HumanTask",
                            NextSteps = new Dictionary<string, string> { { "TaskCompleted", "END" } }
                        }
                    } 
                }
            };
            var wc = new WorkflowClass(_tenantId, "TaskWorkflow", "1.0.0", bp);
            wc.Publish();
            context.WorkflowClasses.Add(wc);
            workflowClassId = wc.Id;

            // Seed WorkflowDefinition (Engine)
            var definition = new WorkflowDefinition(_tenantId, "TaskWorkflow", 1, "Step1");
            
            var step1 = new WorkflowStepDefinition("Step1", WorkflowStepType.Command);
            step1.NextSteps.Add("EVT-START", "UserTask");
            definition.AddStep(step1);
            
            var step2 = new WorkflowStepDefinition("UserTask", WorkflowStepType.HumanTask);
            step2.NextSteps.Add("TaskCompleted", "END");
            definition.AddStep(step2);

            definition.Publish();
            context.WorkflowDefinitions.Add(definition);

            await context.SaveChangesAsync();
        }

        // 2. Start Workflow
        var startCommand = new StartWorkflowCommand(
            _tenantId,
            null,
            "TaskWorkflow",
            1,
            workflowClassId,
            "Step1"
        );
        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        startResponse.EnsureSuccessStatusCode();
        var startResult = await startResponse.Content.ReadFromJsonAsync<StartWorkflowResponse>();
        var instanceId = startResult!.WorkflowInstanceId;

        // 3. Move to Human Task
        var publishCommand = new PublishEventCommand(
            _tenantId,
            instanceId,
            "EVT-START"
        );
        var pubResponse = await _client.PostAsJsonAsync("/api/events/publish", publishCommand);
        pubResponse.EnsureSuccessStatusCode();

        // 4. Verify Task is Waiting
        var tasksResponse = await _client.GetAsync("/api/tasks");
        tasksResponse.EnsureSuccessStatusCode();
        var tasks = await tasksResponse.Content.ReadFromJsonAsync<List<TaskDto>>();
        
        Assert.NotNull(tasks);
        Assert.Contains(tasks, t => t.TaskId == instanceId && t.Status == "Waiting" && t.CurrentStep == "UserTask");

        // DEBUG: Verify GetTask works
        var getTaskResponse = await _client.GetAsync($"/api/tasks/{instanceId}");
        if (!getTaskResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"DEBUG: GetTask({instanceId}) Failed: {getTaskResponse.StatusCode}");
        }
        getTaskResponse.EnsureSuccessStatusCode();

        // 5. Complete Task
        var completeUrl = $"/api/tasks/{instanceId}/complete";
        Console.WriteLine($"DEBUG: Calling {completeUrl}");
        // Use empty JSON content instead of null
        var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var completeResponse = await _client.PostAsync(completeUrl, content);
        if (!completeResponse.IsSuccessStatusCode)
        {
            var error = await completeResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"DEBUG: CompleteTask Failed: {completeResponse.StatusCode} - {error}");
        }
        completeResponse.EnsureSuccessStatusCode();

        // 6. Verify Workflow Completed
        var wfResponse = await _client.GetAsync($"/api/workflows/{instanceId}");
        wfResponse.EnsureSuccessStatusCode();
        var wfState = await wfResponse.Content.ReadFromJsonAsync<WorkflowInstanceResponseDto>();
        
        Assert.Equal("Completed", wfState!.Status);
    }
}
