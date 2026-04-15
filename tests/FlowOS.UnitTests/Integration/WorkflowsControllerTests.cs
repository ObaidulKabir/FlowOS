using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.DTOs;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using FlowOS.UnitTests.Workflows;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.UnitTests.Integration;

public class WorkflowsControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public WorkflowsControllerTests(CustomWebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_WorkflowsController_" + Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var descriptor in contextDescriptors)
                {
                    services.Remove(descriptor);
                }

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var descriptor in optionsDescriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddScoped<FlowOSDbContext>(_ =>
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
        SetHeaders(_tenantId);
    }

    private void SetHeaders(Guid? tenantId, string role = "Admin")
    {
        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");

        _client.DefaultRequestHeaders.Add("X-Mock-Role", role);

        if (tenantId.HasValue)
        {
            _client.DefaultRequestHeaders.Add("x-tenant-id", tenantId.Value.ToString());
        }
    }

    private async Task<(Guid DefinitionId, Guid WorkflowClassId)> SeedRunnableWorkflowAsync(Guid tenantId, string name = "RunnableFlow")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

        var adminRole = new Role(tenantId, "Admin");
        adminRole.AddPermission("workflow.start");
        db.Roles.Add(adminRole);

        var workflowClass = new FlowOS.Domain.Entities.WorkflowClass(
            tenantId,
            name,
            "1.0.0",
            new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
            {
                StateMachine = new FlowOS.Domain.Blueprints.StateMachineBlueprint
                {
                    InitialState = "Start",
                    States = new List<string> { "Start" }
                },
                Workflow = new FlowOS.Domain.Blueprints.WorkflowBlueprint
                {
                    StartStepId = "Start",
                    Steps = new List<FlowOS.Domain.Blueprints.StepBlueprint>
                    {
                        new()
                        {
                            StepId = "Start",
                            StepType = "Command",
                            NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                        }
                    }
                }
            });
        db.WorkflowClasses.Add(workflowClass);

        var definition = new WorkflowDefinition(tenantId, name, 1, "Start");
        var step = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
        step.NextSteps.Add("Default", "END");
        definition.AddStep(step);
        definition.Publish();
        db.WorkflowDefinitions.Add(definition);

        await db.SaveChangesAsync();
        return (definition.Id, workflowClass.Id);
    }

    [Fact]
    public async Task List_WithoutTenantHeader_ReturnsUnauthorized()
    {
        SetHeaders(null);

        var response = await _client.GetAsync("/api/workflows");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ForDifferentTenant_ReturnsNotFound()
    {
        var otherTenantId = Guid.NewGuid();
        Guid workflowId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var definition = new WorkflowDefinition(otherTenantId, "OtherTenantFlow", 1, "Start");
            var step = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
            step.NextSteps.Add("Default", "END");
            definition.AddStep(step);
            definition.Publish();
            db.WorkflowDefinitions.Add(definition);

            var instance = new WorkflowInstance(otherTenantId, definition.Id, Guid.NewGuid(), definition.Version, "Start", Guid.NewGuid());
            db.WorkflowInstances.Add(instance);
            await db.SaveChangesAsync();
            workflowId = instance.Id;
        }

        SetHeaders(_tenantId);

        var response = await _client.GetAsync($"/api/workflows/{workflowId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ForCurrentTenant_ReturnsWorkflow()
    {
        Guid workflowId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var definition = new WorkflowDefinition(_tenantId, "CurrentTenantFlow", 1, "Start");
            var step = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
            step.NextSteps.Add("Default", "END");
            definition.AddStep(step);
            definition.Publish();
            db.WorkflowDefinitions.Add(definition);

            var instance = new WorkflowInstance(_tenantId, definition.Id, Guid.NewGuid(), definition.Version, "Start", Guid.NewGuid());
            db.WorkflowInstances.Add(instance);
            await db.SaveChangesAsync();
            workflowId = instance.Id;
        }

        var response = await _client.GetAsync($"/api/workflows/{workflowId}");

        response.EnsureSuccessStatusCode();
        var workflow = await response.Content.ReadFromJsonAsync<WorkflowSummaryDto>();
        Assert.NotNull(workflow);
        Assert.Equal(workflowId, workflow.Id);
    }

    [Fact]
    public async Task Start_WhenCommandTenantDiffers_UsesCurrentUserTenant()
    {
        var seeded = await SeedRunnableWorkflowAsync(_tenantId, "TenantOverrideFlow");
        var mismatchedTenantId = Guid.NewGuid();
        var command = new StartWorkflowCommand(
            mismatchedTenantId,
            seeded.DefinitionId,
            null,
            1,
            seeded.WorkflowClassId,
            "Start",
            Guid.NewGuid());

        var response = await _client.PostAsJsonAsync("/api/workflows/start", command);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<WorkflowStartResponse>();
        Assert.NotNull(payload);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var createdInstance = await db.WorkflowInstances.FindAsync(payload.WorkflowInstanceId);

        Assert.NotNull(createdInstance);
        Assert.Equal(_tenantId, createdInstance.TenantId);
        Assert.NotEqual(mismatchedTenantId, createdInstance.TenantId);
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
}
