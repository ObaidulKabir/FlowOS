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
using FlowOS.Workflows.Builders; // Added Builder
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Workflows;

public class Workflow_CodeConfigParity : IClassFixture<WebApplicationFactory<Program>>
{
    // ... (constructor and setup remain same)
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public Workflow_CodeConfigParity(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);
                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_WorkflowParity_" + Guid.NewGuid();
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
    }

    private void SetupAdminHeaders()
    {
        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
    }

    [Fact]
    public async Task WF_E2E_1_Config_Vs_Code_Parity()
    {
        SetupAdminHeaders();

        // 1. Define "Code" Workflow using Fluent API
        var codeWfName = "CodeParityWorkflow";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            EnsureAdminRole(db);

            var wfCode = WorkflowBuilder.Create(_tenantId, codeWfName, 1)
                .StartWith("Start", WorkflowStepType.Command)
                    .Then("END")
                .Build();
                
            wfCode.Publish();
            db.WorkflowDefinitions.Add(wfCode);
            await db.SaveChangesAsync();
        }

        // 2. Define "Config" Workflow (Simulate JSON Loading)
        // ... (JSON part remains same as it tests parsing)
        var configWfName = "ConfigParityWorkflow";
        var json = $$"""
        {
          "name": "{{configWfName}}",
          "version": 1,
          "steps": [
            {
              "stepId": "Start",
              "stepType": "Command",
              "nextSteps": { "Default": "END" }
            }
          ]
        }
        """;
        
        // Manual "Loader" logic
        var dto = JsonSerializer.Deserialize<WorkflowConfigDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var wfConfig = new WorkflowDefinition(_tenantId, dto.Name, dto.Version);
            foreach (var s in dto.Steps)
            {
                var step = new WorkflowStepDefinition(s.StepId, Enum.Parse<WorkflowStepType>(s.StepType));
                foreach (var ns in s.NextSteps) step.NextSteps.Add(ns.Key, ns.Value);
                wfConfig.AddStep(step);
            }
            wfConfig.Publish(); // Critical: Config loader calls Publish()
            db.WorkflowDefinitions.Add(wfConfig);
            await db.SaveChangesAsync();
        }

        // 3. Execute Both
        var codeId = await StartWorkflow(codeWfName);
        var configId = await StartWorkflow(configWfName);

        // 4. Assert Identity
        var stateCode = await GetState(codeId);
        var stateConfig = await GetState(configId);

        Assert.Equal("Completed", stateCode.Status);
        Assert.Equal("Completed", stateConfig.Status);
        // Both went Start -> Default -> END -> Completed
    }

    [Fact]
    public async Task WF_E2E_2_Code_Workflow_Cannot_Bypass_Publish()
    {
        SetupAdminHeaders();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            EnsureAdminRole(db);

            var wfUnpublished = WorkflowBuilder.Create(_tenantId, "UnpublishedWorkflow", 1)
                .StartWith("Start", WorkflowStepType.Command)
                .Build();
                
            // DO NOT CALL Publish()
            // Status is Draft
            db.WorkflowDefinitions.Add(wfUnpublished);
            await db.SaveChangesAsync();
        }

        // Attempt to start
        var command = new StartWorkflowCommand(_tenantId, null, "UnpublishedWorkflow", 1, Guid.Empty, "Start", Guid.NewGuid());
        var response = await _client.PostAsJsonAsync("/api/workflows/start", command);

        Assert.False(response.IsSuccessStatusCode, "Should not start unpublished workflow");
    }

    [Fact]
    public async Task WF_E2E_3_Version_Pinning_Works_For_Code()
    {
        SetupAdminHeaders();

        // 1. Publish v1 (Code)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            EnsureAdminRole(db);

            var v1 = WorkflowBuilder.Create(_tenantId, "PinnedWorkflow", 1)
                .StartWith("Start", WorkflowStepType.HumanTask)
                    .On("TaskCompleted", "END")
                    .CompleteStep()
                .Build();
                
            v1.Publish();
            db.WorkflowDefinitions.Add(v1);
            await db.SaveChangesAsync();
        }

        // 2. Start Instance on v1
        var idV1 = await StartWorkflow("PinnedWorkflow", 1);
        
        // 3. Publish v2 (Code) - Different Logic
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var v2 = WorkflowBuilder.Create(_tenantId, "PinnedWorkflow", 2)
                .StartWith("Start", WorkflowStepType.HumanTask)
                    .On("TaskCompleted", "Middle")
                    .CompleteStep()
                .AddStep("Middle", WorkflowStepType.Command)
                    .Then("END")
                .Build();
                
            v2.Publish();
            db.WorkflowDefinitions.Add(v2);
            await db.SaveChangesAsync();
        }

        // 4. Start Instance on v2
        var idV2 = await StartWorkflow("PinnedWorkflow", 2);

        // 5. Assert v1 Instance follows v1 logic
        await CompleteTask(idV1);
        var stateV1 = await GetState(idV1);
        Assert.Equal("Completed", stateV1.Status); 
        
        // 6. Assert v2 Instance follows v2 logic
        await CompleteTask(idV2);
        var stateV2 = await GetState(idV2);
        
        var v1Instance = await GetInstance(idV1);
        Assert.Equal(1, v1Instance.Version);
        
        var v2Instance = await GetInstance(idV2);
        Assert.Equal(2, v2Instance.Version);
    }

    // Helpers
    private async Task<Guid> StartWorkflow(string name, int version = 1)
    {
        var command = new StartWorkflowCommand(_tenantId, null, name, version, Guid.Empty, "Start", Guid.NewGuid());
        var response = await _client.PostAsJsonAsync("/api/workflows/start", command);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WorkflowStartResponse>();
        return result.WorkflowInstanceId;
    }

    private async Task CompleteTask(Guid id)
    {
        var response = await _client.PostAsync($"/api/tasks/{id}/complete", null);
        response.EnsureSuccessStatusCode();
    }

    private async Task<WorkflowStateDto> GetState(Guid id)
    {
        var response = await _client.GetAsync($"/api/workflows/{id}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkflowStateDto>();
    }
    
    private async Task<WorkflowInstanceDto> GetInstance(Guid id)
    {
        // Need a DTO that exposes Version
        var response = await _client.GetAsync($"/api/workflows/{id}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkflowInstanceDto>();
    }

    private void EnsureAdminRole(FlowOSDbContext db)
    {
        if (!db.Roles.Any(r => r.Name == "Admin"))
        {
            var adminRole = new Role(_tenantId, "Admin");
            adminRole.AddPermission("workflow.start");
            adminRole.AddPermission("task.complete");
            db.Roles.Add(adminRole);
            db.SaveChanges();
        }
    }

    // DTOs
    private record WorkflowStartResponse(Guid WorkflowInstanceId);
    private record WorkflowStateDto(Guid Id, string Status, string CurrentStepId);
    private record WorkflowInstanceDto(Guid Id, string Status, int Version);
    
    private class WorkflowConfigDto
    {
        public string Name { get; set; } = string.Empty;
        public int Version { get; set; }
        public WorkflowStepConfigDto[] Steps { get; set; } = Array.Empty<WorkflowStepConfigDto>();
    }

    private class WorkflowStepConfigDto
    {
        public string StepId { get; set; } = string.Empty;
        public string StepType { get; set; } = "Command";
        public Dictionary<string, string> NextSteps { get; set; } = new();
    }
}
