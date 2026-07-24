using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using FlowOS.UnitTests.Workflows;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.UnitTests.Integration;

public class EventApiTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public EventApiTests(CustomWebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_EventApi_" + Guid.NewGuid();
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

    private async Task<(Guid InstanceId, Guid DefId)> SeedWorkflowAsync(string startStepId = "StepA")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

        // 1. Role
        // Check if Admin role exists, if not add it
        if (!await db.Roles.AnyAsync(r => r.TenantId == _tenantId && r.Name == "Admin"))
        {
            var role = new Role(_tenantId, "Admin");
            role.Permissions.Add("event.publish.EVT-NEXT");
            role.Permissions.Add("event.publish.EVT-DECIDE");
            db.Roles.Add(role);
        }

        // 2. Event Registry
        if (!await db.EventDefinitions.AnyAsync(e => e.TenantId == _tenantId && e.EventId == "EVT-NEXT"))
        {
            db.EventDefinitions.Add(new EventDefinition("EVT-NEXT", _tenantId, "Next", "Desc", "Cat", EventCategory.System));
            db.EventDefinitions.Add(new EventDefinition("EVT-DECIDE", _tenantId, "Decide", "Desc", "Cat", EventCategory.System));
        }

        // 3. Workflow Definition
        var def = new WorkflowDefinition(_tenantId, "EventTestFlow", 1, startStepId);
        
        // Step A -> EVT-NEXT -> Step B
        var stepA = new WorkflowStepDefinition("StepA", WorkflowStepType.SystemTask);
        stepA.NextSteps.Add("EVT-NEXT", "StepB");
        def.AddStep(stepA);

        // Step B -> EVT-DECIDE -> Decision
        var stepB = new WorkflowStepDefinition("StepB", WorkflowStepType.SystemTask);
        stepB.NextSteps.Add("EVT-DECIDE", "StepDecision");
        def.AddStep(stepB);

        // Decision Step
        var stepDec = new WorkflowStepDefinition("StepDecision", WorkflowStepType.Decision);
        stepDec.Conditions.Add("Amount > 100", "StepHigh");
        stepDec.Conditions.Add("Default", "StepLow"); // Engine logic requires Default in Conditions for fallback
        
        def.AddStep(stepDec);

        // Targets
        def.AddStep(new WorkflowStepDefinition("StepHigh", WorkflowStepType.SystemTask));
        def.AddStep(new WorkflowStepDefinition("StepLow", WorkflowStepType.SystemTask));

        def.Publish();
        db.WorkflowDefinitions.Add(def);

        // 4. Instance
        var instance = new WorkflowInstance(_tenantId, def.Id, Guid.Empty, 1, startStepId, Guid.NewGuid());
        db.WorkflowInstances.Add(instance);

        await db.SaveChangesAsync();
        return (instance.Id, def.Id);
    }

    [Fact]
    public async Task PublishEvent_WithValidPermissions_ShouldAdvanceWorkflow()
    {
        // Arrange
        var (instanceId, _) = await SeedWorkflowAsync("StepA");
        var command = new PublishEventCommand(_tenantId, instanceId, "EVT-NEXT", Guid.NewGuid(), null);

        // Act
        var response = await _client.PostAsJsonAsync("/api/events/publish", command);

        // Assert
        response.EnsureSuccessStatusCode();
        
        // Verify State
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var instance = await db.WorkflowInstances.FindAsync(instanceId);
        Assert.Equal("StepB", instance.CurrentStepId);
    }

    [Fact]
    public async Task PublishEvent_WithMissingPermissions_ShouldFail()
    {
        // Arrange
        var (instanceId, _) = await SeedWorkflowAsync("StepA");
        var command = new PublishEventCommand(_tenantId, instanceId, "EVT-NEXT", Guid.NewGuid(), null);

        // Act - Switch Role
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Guest"); // No permissions

        var response = await _client.PostAsJsonAsync("/api/events/publish", command);

        // Assert - PolicyViolationException is mapped to 403 by ApiExceptionFilterAttribute.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublishEvent_WithPayload_ShouldTriggerDecision_HighValue()
    {
        // Arrange
        var (instanceId, _) = await SeedWorkflowAsync("StepB");
        var payload = new Dictionary<string, object> { { "Amount", 150 } };
        var command = new PublishEventCommand(_tenantId, instanceId, "EVT-DECIDE", Guid.NewGuid(), payload);

        // Act
        var response = await _client.PostAsJsonAsync("/api/events/publish", command);

        // Assert
        response.EnsureSuccessStatusCode();

        // Verify State
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var instance = await db.WorkflowInstances.FindAsync(instanceId);
        Assert.Equal("StepHigh", instance.CurrentStepId); // 150 > 100
    }

    [Fact]
    public async Task PublishEvent_WithPayload_ShouldTriggerDecision_LowValue()
    {
        // Arrange
        var (instanceId, _) = await SeedWorkflowAsync("StepB");
        var payload = new Dictionary<string, object> { { "Amount", 50 } };
        var command = new PublishEventCommand(_tenantId, instanceId, "EVT-DECIDE", Guid.NewGuid(), payload);

        // Act
        var response = await _client.PostAsJsonAsync("/api/events/publish", command);

        // Assert
        response.EnsureSuccessStatusCode();

        // Verify State
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var instance = await db.WorkflowInstances.FindAsync(instanceId);
        Assert.Equal("StepLow", instance.CurrentStepId); // 50 <= 100, goes to Default
    }

    [Fact]
    public async Task PublishEvent_WithUnknownEvent_ShouldReturnBadRequest()
    {
         // Arrange
        var (instanceId, _) = await SeedWorkflowAsync("StepA");
        
        // Grant Root Permission to bypass Policy Check and test Registry Check
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var role = await db.Roles.FirstOrDefaultAsync(r => r.TenantId == _tenantId && r.Name == "Admin");
            if (role != null)
            {
                role.Permissions.Add("event.publish");
                await db.SaveChangesAsync();
            }
        }

        var command = new PublishEventCommand(_tenantId, instanceId, "EVT-UNKNOWN", Guid.NewGuid(), null);

        // Act
        var response = await _client.PostAsJsonAsync("/api/events/publish", command);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("not registered", problem.Detail);
    }

    [Fact]
    public async Task PublishEvent_WhenTransitionFails_ShouldReturnBadRequest_WithReason()
    {
        // Arrange
        // Create a workflow that is stuck in a step with NO transitions
        var (instanceId, _) = await SeedWorkflowAsync("StepLow"); // StepLow has no next steps in SeedWorkflowAsync

        // Grant Permission
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var role = await db.Roles.FirstOrDefaultAsync(r => r.TenantId == _tenantId && r.Name == "Admin");
            if (role != null)
            {
                role.Permissions.Add("event.publish.EVT-NEXT"); // Allow EVT-NEXT even if it won't work
                await db.SaveChangesAsync();
            }
        }

        var command = new PublishEventCommand(_tenantId, instanceId, "EVT-NEXT", Guid.NewGuid(), null);

        // Act
        var response = await _client.PostAsJsonAsync("/api/events/publish", command);

        // Assert
        // Should be 400 because we map InvalidOperationException to 400 in Filter
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("transition failed", problem.Detail); // "Workflow transition failed: No transition defined..."
    }
}
