using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.DTOs; // Back in
using FlowOS.Application.DTOs.Governance;
using FlowOS.Application.DTOs.Workflows;
using FlowOS.Infrastructure.Persistence;
using FlowOS.UnitTests.Workflows;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowOS.Domain.Entities; 
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums; // Add this for WorkflowClassStatus
using FlowOS.Security.Models; // Add this for Role

namespace FlowOS.UnitTests.Integration;

public class WorkflowApiTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public WorkflowApiTests(CustomWebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_WorkflowApi_" + Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                services.AddScoped<FlowOSDbContext>(provider => 
                {
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(dbName)
                        .EnableSensitiveDataLogging()
                        .Options;
                    
                    return new TestFlowOSDbContext(options);
                });
            });
        });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
    }

    [Fact]
    public async Task GetWorkflows_ShouldReturnRunningWorkflows()
    {
        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            // 1. Seed WorkflowClass
            var bp = new WorkflowClassBlueprint
            {
                // Minimal valid blueprint to avoid validation errors if triggered, but for DB seed it might not matter
                StateMachine = new StateMachineBlueprint { InitialState = "Start", States = new List<string> { "Start" } },
                Workflow = new WorkflowBlueprint 
                { 
                    StartStepId = "Start",
                    Steps = new List<StepBlueprint> { new StepBlueprint { StepId = "Start", StepType = "Command" } } 
                }
            };
            var wc = new WorkflowClass(_tenantId, "ApiTestClass", "1.0.0", bp);
            db.WorkflowClasses.Add(wc);

            // 2. Seed Definition (Still needed? Instance refers to it?)
            // WorkflowInstance has WorkflowDefinitionId.
            var def = new WorkflowDefinition(_tenantId, "ApiTestFlow", 1, "Start"); // Add StartStepId
            var step = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
            step.NextSteps.Add("Default", "END");
            def.AddStep(step);
            def.Publish();
            db.WorkflowDefinitions.Add(def);

            // 3. Seed Instance
            var runningInstance = new WorkflowInstance(_tenantId, def.Id, wc.Id, def.Version, "Start", Guid.NewGuid());
            db.WorkflowInstances.Add(runningInstance);
            
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetFromJsonAsync<List<WorkflowInstanceResponseDto>>("/api/workflows?status=Running");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        Assert.Contains(response, w => w.Status == "Running");
        Assert.Equal(1, response.Count);
    }

    [Fact]
    public async Task CreateInstance_ShouldReturnCreated()
    {
        // 1. Setup Data
        WorkflowClass wc;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var bp = new WorkflowClassBlueprint
            {
                StateMachine = new StateMachineBlueprint { InitialState = "Start", States = new List<string> { "Start" } },
                Workflow = new WorkflowBlueprint 
                { 
                    StartStepId = "Start", 
                    Steps = new List<StepBlueprint> 
                    { 
                        new StepBlueprint 
                        { 
                            StepId = "Start", 
                            StepType = "Command",
                            NextSteps = new Dictionary<string, string> { { "Default", "END" } } // Added exit path
                        } 
                    } 
                }
            };
            wc = new WorkflowClass(_tenantId, "RunnableClass", "1.0.0", bp);
            wc.Publish();
            db.WorkflowClasses.Add(wc);
            
            // Seed Admin Role for "workflow.start" capability
            var adminRole = new Role(_tenantId, "Admin");
            adminRole.Permissions.Add("workflow.start");
            db.Roles.Add(adminRole);
            
            // Seed corresponding WorkflowDefinition (Engine requirement)
            var def = new WorkflowDefinition(_tenantId, "RunnableClass", 1, "Start");
            var step = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
            step.NextSteps.Add("Default", "END");
            def.AddStep(step);
            def.Publish();
            db.WorkflowDefinitions.Add(def);
            
            await db.SaveChangesAsync();
        }

        // 2. Call API
        // NOTE: The user ID/tenant ID in the request context comes from the TestAuthHandler.
        // We ensure we send a command that aligns with that identity.
        // We also need to ensure the user has the 'Admin' role as per X-Mock-Role header if that's what's required,
        // OR that the endpoint doesn't require specific permissions beyond authentication if we are just starting it.
        // The previous error 403 suggests authorization failure.
        // Let's verify if the policy requirement for StartWorkflow matches the user's role/permissions.
        // Assuming default setup, let's try to align the tenantId explicitly.
        
        var command = new StartWorkflowCommand(
            _tenantId, 
            null, // WorkflowDefinitionId
            null, // WorkflowName
            null, // Version
            wc.Id, // WorkflowClassId
            "Start", // InitialStepId
            Guid.NewGuid() // CorrelationId
        );

        // Ensure the client sends the correct tenant ID header
        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());

        var response = await _client.PostAsJsonAsync("/api/workflows/start", command);

        // 3. Assert
        response.EnsureSuccessStatusCode();
        // The API returns { WorkflowInstanceId = "..." }
        var result = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(result);
        Assert.NotNull(result.GetProperty("workflowInstanceId"));
    }

    [Fact]
    public async Task CreateDraft_ShouldReturnDraftStatus()
    {
        var request = new CreateWorkflowClassRequest
        {
            Name = "NewApiDraft",
            Version = "1.0.0",
            Definition = new WorkflowClassBlueprint
            {
                StateMachine = new StateMachineBlueprint { InitialState = "S1", States = new List<string> { "S1" } },
                Workflow = new WorkflowBlueprint 
                { 
                    StartStepId = "S1", 
                    Steps = new List<StepBlueprint> 
                    { 
                        new StepBlueprint 
                        { 
                            StepId = "S1", 
                            StepType = "Command",
                            RequiredRoles = new List<string> { "Admin" },
                            NextSteps = new Dictionary<string, string> { { "EVT-DONE", "END" } }
                        } 
                    } 
                },
                Events = new List<EventBlueprint> { new EventBlueprint { EventId = "EVT-DONE", Name = "Done" } },
                Roles = new List<RoleBlueprint> { new RoleBlueprint { Name = "Admin" } }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/workflow-classes", request);
        
        response.EnsureSuccessStatusCode();
        var wc = await response.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
        
        Assert.Equal("NewApiDraft", wc.Name);
        Assert.Equal(WorkflowClassStatus.Draft, wc.Status);
    }

    [Fact]
    public async Task CreateNewVersion_ShouldReturnDraft()
    {
        // 1. Create & Publish v1
        var createResp = await _client.PostAsJsonAsync("/api/workflow-classes", new CreateWorkflowClassRequest
        {
            Name = "VersioningTest",
            Version = "1.0.0",
            Definition = new WorkflowClassBlueprint 
            { 
                StateMachine = new StateMachineBlueprint { InitialState = "S1", States = new List<string> { "S1" } }, 
                Workflow = new WorkflowBlueprint 
                { 
                    StartStepId = "S1", 
                    Steps = new List<StepBlueprint> 
                    { 
                        new StepBlueprint 
                        { 
                            StepId = "S1", 
                            StepType = "Command",
                            RequiredRoles = new List<string> { "Admin" },
                            NextSteps = new Dictionary<string, string> { { "EVT-DONE", "END" } }
                        } 
                    } 
                },
                Events = new List<EventBlueprint> { new EventBlueprint { EventId = "EVT-DONE", Name = "Done" } },
                Roles = new List<RoleBlueprint> { new RoleBlueprint { Name = "Admin" } }
            }
        });
        
        createResp.EnsureSuccessStatusCode(); // Ensure v1 created successfully
        var v1 = await createResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
        await _client.PostAsync($"/api/workflow-classes/{v1.Id}/publish", null);

        // 2. Create v2
        var v2Resp = await _client.PostAsync($"/api/workflow-classes/{v1.Id}/new-version", null);
        v2Resp.EnsureSuccessStatusCode();
        var v2 = await v2Resp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();

        // 3. Assert
        Assert.Equal(WorkflowClassStatus.Draft, v2.Status);
        Assert.Equal(v1.Id, v2.PreviousVersionId);
        Assert.NotEqual(v1.Id, v2.Id);
    }
}
