using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Blueprints;
using Microsoft.Extensions.DependencyInjection;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using FlowOS.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FlowOS.E2E.Tests.ApiTests;

public class AgentsTests : IClassFixture<FlowOSWebApplicationFactory>
{
    private readonly FlowOSWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public AgentsTests(FlowOSWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
    }

    [Fact]
    public async Task PublishInsight_ShouldWork()
    {
        // 0. Seed Admin Role with Permissions using DbContext directly
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var role = new Role(_tenantId, "Admin");
            role.AddPermission("workflow.create");
            role.AddPermission("workflow.publish");
            role.AddPermission("workflow.start");
            role.AddPermission("agent.insight.publish");
            context.Roles.Add(role);
            await context.SaveChangesAsync();
        }

        // 1. Setup: Create and Publish a Workflow Class
        var wc = await CreateAndPublishWorkflowClass();
        
        // 2. Start Workflow
        var startCmd = new StartWorkflowCommand(
            _tenantId, 
            null, 
            null, 
            null, 
            wc.Id, 
            "Step1", 
            Guid.NewGuid()
        );
        
        var startResp = await _client.PostAsJsonAsync("/api/workflows/start", startCmd);
        startResp.EnsureSuccessStatusCode();
        var startResult = await startResp.Content.ReadFromJsonAsync<WorkflowStartResult>();
        var instanceId = startResult.WorkflowInstanceId;

        // 3. Publish Insight
        var insightReq = new
        {
            WorkflowInstanceId = instanceId,
            AgentId = "TestAgent",
            Insight = "This is a test insight",
            ContextObjective = "Testing",
            CorrelationId = Guid.NewGuid()
        };

        var insightResp = await _client.PostAsJsonAsync("/api/agents/insight", insightReq);
            if (!insightResp.IsSuccessStatusCode)
            {
                var errorContent = await insightResp.Content.ReadAsStringAsync();
                throw new Exception($"Failed with status {insightResp.StatusCode}. Content: {errorContent}");
            }
            insightResp.EnsureSuccessStatusCode();
        
        // 4. Verify (Optional: Check DB or if endpoint returns specific data)
        // For now, 200 OK is sufficient as per controller logic.
    }

    private async Task<WorkflowClassResponseDto> CreateAndPublishWorkflowClass()
    {
        var createRequest = new CreateWorkflowClassRequest
        {
            Name = "AgentTestFlow",
            Version = "1.0.0",
            Definition = new WorkflowClassBlueprint
            {
                Events = new List<EventBlueprint>(),
                StateMachine = new StateMachineBlueprint
                {
                    InitialState = "Start",
                    States = new List<string> { "Start", "End" }
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Step1",
                    Steps = new List<StepBlueprint>
                    {
                        new() { StepId = "Step1", StepType = "End" }
                    }
                }
            }
        };

        var createResp = await _client.PostAsJsonAsync("/api/workflow-classes", createRequest);
        createResp.EnsureSuccessStatusCode();
        var draft = await createResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();

        var pubResp = await _client.PostAsync($"/api/workflow-classes/{draft.Id}/publish", null);
        pubResp.EnsureSuccessStatusCode();
        
        return await pubResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
    }

    private class WorkflowStartResult { public Guid WorkflowInstanceId { get; set; } }
}
