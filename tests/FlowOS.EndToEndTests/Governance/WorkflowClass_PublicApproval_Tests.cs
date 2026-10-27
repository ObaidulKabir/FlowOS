using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Governance;

/// <summary>
/// A Public workflow class is visible to every tenant, so promoting one is a platform decision:
/// a tenant's own Admin must not be able to do it.
/// </summary>
public class WorkflowClass_PublicApproval_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid PlatformTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly WebApplicationFactory<Program> _factory;

    public WorkflowClass_PublicApproval_Tests(WebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_E2E_PublicApproval_" + Guid.NewGuid();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList()) services.Remove(d);
                foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList()) services.Remove(d);
                services.AddScoped<FlowOSDbContext>(_ => new TestFlowOSDbContext(
                    new DbContextOptionsBuilder<FlowOSDbContext>().UseInMemoryDatabase(dbName).Options));
            });
        });
    }

    private async Task<Guid> SeedSharedClassAsync(Guid ownerTenant)
    {
        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IWorkflowClassManager>();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new() { new EventBlueprint { EventId = "EVT-GO", Name = "Go" } },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Start",
                States = new() { "Start", "End" },
                Transitions = new() { new TransitionBlueprint { FromState = "Start", ToState = "End", EventId = "EVT-GO" } }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Start",
                Steps = new()
                {
                    new StepBlueprint { StepId = "Start", StepType = "Command", NextSteps = new() { { "EVT-GO", "End" } } },
                    new StepBlueprint { StepId = "End", StepType = "End" }
                }
            }
        };
        var workflowClass = new WorkflowClass(ownerTenant, "SharedForApproval_" + Guid.NewGuid().ToString("N")[..6], "1.0.0", blueprint);
        Assert.True(manager.Publish(workflowClass).IsValid);
        Assert.True(manager.SubmitForReview(workflowClass).IsValid);
        db.WorkflowClasses.Add(workflowClass);
        await db.SaveChangesAsync();
        return workflowClass.Id;
    }

    private async Task<HttpResponseMessage> ApproveAs(Guid tenantId, Guid workflowClassId)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workflow-classes/{workflowClassId}/approve");
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        request.Headers.Add("X-Mock-Role", "Admin");
        return await client.SendAsync(request);
    }

    private async Task<WorkflowClassStatus> StatusOf(Guid workflowClassId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        return (await db.WorkflowClasses.AsNoTracking().SingleAsync(c => c.Id == workflowClassId)).Status;
    }

    [Fact]
    public async Task A_tenant_admin_cannot_make_a_class_public()
    {
        var classId = await SeedSharedClassAsync(OtherTenant);

        var response = await ApproveAs(OtherTenant, classId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(WorkflowClassStatus.Shared, await StatusOf(classId));
    }

    [Fact]
    public async Task A_platform_administrator_can_make_a_class_public()
    {
        var classId = await SeedSharedClassAsync(OtherTenant);

        var response = await ApproveAs(PlatformTenant, classId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(WorkflowClassStatus.Public, await StatusOf(classId));
    }
}
