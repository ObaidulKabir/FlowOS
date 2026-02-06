using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
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

public class RuntimeTests : IClassFixture<FlowOSWebApplicationFactory>
{
    private readonly FlowOSWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public RuntimeTests(FlowOSWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
    }

    [Fact]
    public async Task StartWorkflow_And_Advance_ShouldWork()
    {
        Guid workflowClassId;

        // 1. Seed WorkflowDefinition and EventDefinitions
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            // Seed Admin Role for this tenant
            var adminRole = new Role(_tenantId, "Admin");
            adminRole.AddPermission("workflow.start");
            adminRole.AddPermission("workflow.create");
            adminRole.AddPermission("workflow.read");
            adminRole.AddPermission("event.publish");
            adminRole.AddPermission("task.complete");
            context.Roles.Add(adminRole);

            // Seed Events
            var evtProcess = new EventDefinition(
                "EVT-PROCESS", _tenantId, "Process Event", "Triggers process", "System", EventCategory.System);
            evtProcess.Publish();
            context.EventDefinitions.Add(evtProcess);

            var evtComplete = new EventDefinition(
                "EVT-COMPLETE", _tenantId, "Complete Event", "Completes process", "System", EventCategory.System);
            evtComplete.Publish();
            context.EventDefinitions.Add(evtComplete);

            // Seed WorkflowClass (Governance)
            var bp = new WorkflowClassBlueprint
            {
                Events = new List<EventBlueprint> 
                { 
                    new EventBlueprint { EventId = "EVT-PROCESS", Name = "Process" },
                    new EventBlueprint { EventId = "EVT-COMPLETE", Name = "Complete" }
                },
                StateMachine = new StateMachineBlueprint { InitialState = "Step1", States = new List<string> { "Step1", "Step2", "Completed" } },
                Workflow = new WorkflowBlueprint 
                { 
                    StartStepId = "Step1", 
                    Steps = new List<StepBlueprint> 
                    { 
                        new StepBlueprint 
                        { 
                            StepId = "Step1", 
                            StepType = "Command",
                            NextSteps = new Dictionary<string, string> { { "EVT-PROCESS", "Step2" } }
                        },
                        new StepBlueprint 
                        { 
                            StepId = "Step2", 
                            StepType = "Command",
                            NextSteps = new Dictionary<string, string> { { "EVT-COMPLETE", "END" } }
                        }
                    } 
                }
            };
            var wc = new WorkflowClass(_tenantId, "RuntimeTestWorkflow", "1.0.0", bp);
            wc.Publish();
            context.WorkflowClasses.Add(wc);
            workflowClassId = wc.Id;

            // Seed WorkflowDefinition (Engine)
            var definition = new WorkflowDefinition(_tenantId, "RuntimeTestWorkflow", 1, "Step1");
            
            var step1 = new WorkflowStepDefinition("Step1", WorkflowStepType.Command);
            step1.NextSteps.Add("EVT-PROCESS", "Step2");
            definition.AddStep(step1);
            
            var step2 = new WorkflowStepDefinition("Step2", WorkflowStepType.Command);
            step2.NextSteps.Add("EVT-COMPLETE", "END");
            definition.AddStep(step2);

            definition.Publish();
            context.WorkflowDefinitions.Add(definition);
            
            await context.SaveChangesAsync();

            // Verify Role Saved
            var savedRole = context.Roles.FirstOrDefault(r => r.TenantId == _tenantId && r.Name == "Admin");
            if (savedRole == null) Console.WriteLine("DEBUG: Role not found in DB!");
            else Console.WriteLine($"DEBUG: Role found. Permissions: {string.Join(", ", savedRole.Permissions)}");
        }

        // 2. Start Workflow
        var startCommand = new StartWorkflowCommand(
            _tenantId,
            null,
            "RuntimeTestWorkflow",
            1,
            workflowClassId, // Pass WorkflowClassId to ensure it is linked
            "Step1" // Explicitly set InitialStepId to match definition
        );

        var startResponse = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        if (!startResponse.IsSuccessStatusCode)
        {
            var error = await startResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"StartWorkflow Failed: {startResponse.StatusCode} - {error}");
        }
        startResponse.EnsureSuccessStatusCode();
        var startResult = await startResponse.Content.ReadFromJsonAsync<StartWorkflowResponse>();
        var instanceId = startResult!.WorkflowInstanceId;
        Console.WriteLine($"DEBUG: StartWorkflow Succeeded. InstanceId: {instanceId}");

        // 3. Verify Initial State
        var listResponse = await _client.GetAsync("/api/workflows");
        listResponse.EnsureSuccessStatusCode();
        var instances = await listResponse.Content.ReadFromJsonAsync<List<WorkflowInstanceResponseDto>>();
        
        var instance = instances!.FirstOrDefault(i => i.WorkflowId == instanceId);
        Assert.NotNull(instance);
        Assert.Equal("Step1", instance.CurrentStep);
        Assert.Equal("Running", instance.Status);
        Console.WriteLine("DEBUG: Initial State Verified");

        // 4. Publish Event to Advance
        var publishCommand = new PublishEventCommand(
            _tenantId,
            instanceId,
            "EVT-PROCESS"
        );
        
        var publishResponse = await _client.PostAsJsonAsync("/api/events/publish", publishCommand);
        if (!publishResponse.IsSuccessStatusCode)
        {
            var error = await publishResponse.Content.ReadAsStringAsync();
            throw new Exception($"PublishEvent Failed: {publishResponse.StatusCode} - {error}");
        }
        publishResponse.EnsureSuccessStatusCode();
        Console.WriteLine("DEBUG: PublishEvent Succeeded.");

        // 5. Verify New State (Step2)
        listResponse = await _client.GetAsync("/api/workflows");
        instances = await listResponse.Content.ReadFromJsonAsync<List<WorkflowInstanceResponseDto>>();
        instance = instances!.First(i => i.WorkflowId == instanceId);
        Assert.Equal("Step2", instance.CurrentStep);
        Console.WriteLine("DEBUG: New State Verified (Step2)");
        
        // 6. Complete
        var completeCommand = new PublishEventCommand(
            _tenantId,
            instanceId,
            "EVT-COMPLETE"
        );
        var completeResponse = await _client.PostAsJsonAsync("/api/events/publish", completeCommand);
        if (!completeResponse.IsSuccessStatusCode)
        {
            var error = await completeResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"DEBUG: CompleteEvent Failed: {completeResponse.StatusCode} - {error}");
        }
        completeResponse.EnsureSuccessStatusCode();
        
        // 7. Verify Completion
        listResponse = await _client.GetAsync("/api/workflows");
        instances = await listResponse.Content.ReadFromJsonAsync<List<WorkflowInstanceResponseDto>>();
        instance = instances!.First(i => i.WorkflowId == instanceId);
        Assert.Equal("Completed", instance.Status);
        Console.WriteLine("DEBUG: Completion Verified");
    }
}

public record StartWorkflowResponse(Guid WorkflowInstanceId);
