using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FlowOS.E2E.Tests.ApiTests;

public class WorkflowClassesTests : IClassFixture<FlowOSWebApplicationFactory>
{
    private readonly FlowOSWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public WorkflowClassesTests(FlowOSWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        // Mock Auth Header
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
    }

    [Fact]
    public async Task CreateAndPublishWorkflowClass_ShouldWork()
    {
        // 1. Create Draft
        var createRequest = new CreateWorkflowClassRequest
        {
            Name = "E2E Test Workflow",
            Version = "1.0.0",
            Definition = new WorkflowClassBlueprint
            {
                Events = new List<EventBlueprint>
                {
                    new() { EventId = "EVT-PROCESS", Name = "Process", Category = EventCategory.System }
                },
                StateMachine = new StateMachineBlueprint
                {
                    EntityType = "E2E_Entity",
                    InitialState = "New",
                    States = new List<string> { "New", "Processed" },
                    Transitions = new List<TransitionBlueprint>
                    {
                        new() { FromState = "New", ToState = "Processed", EventId = "EVT-PROCESS" }
                    }
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Step1",
                    Steps = new List<StepBlueprint>
                    {
                        new() 
                        { 
                            StepId = "Step1", 
                            StepType = "Action", 
                            NextSteps = new Dictionary<string, string> { { "EVT-PROCESS", "Step2" } }
                        },
                        new()
                        {
                            StepId = "Step2",
                            StepType = "End"
                        }
                    }
                }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/workflow-classes", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
        
        created.Should().NotBeNull();
        created!.Status.Should().Be(WorkflowClassStatus.Draft);
        created.Id.Should().NotBeEmpty();

        // 2. Publish
        var publishResponse = await _client.PostAsync($"/api/workflow-classes/{created.Id}/publish", null);
        
        // If validation fails, we want to see why
        if (!publishResponse.IsSuccessStatusCode)
        {
             var error = await publishResponse.Content.ReadAsStringAsync();
             throw new Exception($"Publish failed: {error}");
        }

        publishResponse.EnsureSuccessStatusCode();
        var published = await publishResponse.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();

        published!.Status.Should().Be(WorkflowClassStatus.Published);
    }
}
