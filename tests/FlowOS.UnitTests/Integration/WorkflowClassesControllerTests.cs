using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.UnitTests.Workflows;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.UnitTests.Integration;

public class WorkflowClassesControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public WorkflowClassesControllerTests(CustomWebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_WorkflowClassesController_" + Guid.NewGuid();

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

    private static CreateWorkflowClassRequest CreateValidRequest(string name = "TestWorkflow", string version = "1.0.0")
    {
        return new CreateWorkflowClassRequest
        {
            Name = name,
            Version = version,
            Definition = new WorkflowClassBlueprint
            {
                Events = new List<EventBlueprint>
                {
                    new() { EventId = "EVT-DONE", Name = "Done" }
                },
                StateMachine = new StateMachineBlueprint
                {
                    InitialState = "S1",
                    States = new List<string> { "S1" }
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "S1",
                    Steps = new List<StepBlueprint>
                    {
                        new()
                        {
                            StepId = "S1",
                            StepType = "Command",
                            NextSteps = new Dictionary<string, string> { { "EVT-DONE", "END" } }
                        }
                    }
                }
            }
        };
    }

    private static WorkflowClass CreateWorkflowClass(Guid tenantId, string name = "SeededWorkflow", string version = "1.0.0")
    {
        return new WorkflowClass(tenantId, name, version, CreateValidRequest(name, version).Definition);
    }

    [Fact]
    public async Task CreateDraft_WithoutTenantHeader_ReturnsUnauthorized()
    {
        SetHeaders(null);

        var response = await _client.PostAsJsonAsync("/api/workflow-classes", CreateValidRequest("MissingTenant"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_PrivateWorkflowFromAnotherTenant_ReturnsForbidden()
    {
        var otherTenantId = Guid.NewGuid();
        WorkflowClass workflowClass;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            workflowClass = CreateWorkflowClass(otherTenantId, "OtherTenantPrivate");
            db.WorkflowClasses.Add(workflowClass);
            await db.SaveChangesAsync();
        }

        SetHeaders(_tenantId);

        var response = await _client.GetAsync($"/api/workflow-classes/{workflowClass.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDraft_WhenWorkflowIsPublished_ReturnsBadRequest()
    {
        WorkflowClass workflowClass;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            workflowClass = CreateWorkflowClass(_tenantId, "PublishedWorkflow");

            var manager = new WorkflowClassManager();
            manager.Publish(workflowClass);

            db.WorkflowClasses.Add(workflowClass);
            await db.SaveChangesAsync();
        }

        var response = await _client.PutAsJsonAsync(
            $"/api/workflow-classes/{workflowClass.Id}",
            CreateValidRequest("UpdatedName"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Only Drafts can be updated", content);
    }

    [Fact]
    public async Task Lint_WithEmptyJsonContent_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/workflow-classes/lint", new LintRequestDto());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lint_WithInvalidJson_ReturnsSyntaxErrors()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/workflow-classes/lint",
            new LintRequestDto { JsonContent = "{ invalid json" });

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("JSON-001", content);
    }

    [Fact]
    public async Task CopyToTenant_WhenTargetTenantDiffersFromCurrentUser_ReturnsForbidden()
    {
        var sourceTenantId = Guid.NewGuid();
        var requestedTenantId = Guid.NewGuid();
        WorkflowClass workflowClass;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            workflowClass = CreateWorkflowClass(sourceTenantId, "PublicTemplate");

            var manager = new WorkflowClassManager();
            manager.Publish(workflowClass);
            manager.SubmitForReview(workflowClass);
            manager.ApproveAsPublic(workflowClass);

            db.WorkflowClasses.Add(workflowClass);
            await db.SaveChangesAsync();
        }

        SetHeaders(_tenantId);

        var response = await _client.PostAsJsonAsync(
            $"/api/workflow-classes/{workflowClass.Id}/copy",
            new CopyWorkflowClassRequest { NewTenantId = requestedTenantId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
