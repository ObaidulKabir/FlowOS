using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Infrastructure;

/// <summary>Applications (2/4): workflows belong to an application.</summary>
public class TenantApplicationWorkflowTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static FlowOSDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"tenant-app-workflows-{Guid.NewGuid()}")
            .Options);

    private static WorkflowClass AddWorkflow(FlowOSDbContext db, string name, string version, Guid? tenantId = null)
    {
        var workflowClass = new WorkflowClass(tenantId ?? TenantId, name, version, new WorkflowClassBlueprint());
        db.WorkflowClasses.Add(workflowClass);
        return workflowClass;
    }

    [Fact]
    public async Task Assign_moves_every_version_of_the_workflow_together()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);
        var app = await service.CreateAsync(TenantId, "Finance Approvals", null);
        var v1 = AddWorkflow(db, "ExpenseApproval", "1.0.0");
        var v2 = AddWorkflow(db, "ExpenseApproval", "2.0.0");
        var other = AddWorkflow(db, "LoanUnderwritingFlow", "1.0.0");
        await db.SaveChangesAsync();

        var moved = await service.AssignWorkflowClassAsync(TenantId, v1.Id, app.Id);

        Assert.Equal(2, moved);
        Assert.Equal(app.Id, (await db.WorkflowClasses.SingleAsync(w => w.Id == v1.Id)).ApplicationId);
        Assert.Equal(app.Id, (await db.WorkflowClasses.SingleAsync(w => w.Id == v2.Id)).ApplicationId);
        Assert.Null((await db.WorkflowClasses.SingleAsync(w => w.Id == other.Id)).ApplicationId);
    }

    [Fact]
    public async Task Assign_with_null_unassigns_the_workflow()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);
        var app = await service.CreateAsync(TenantId, "Commerce", null);
        var workflow = AddWorkflow(db, "OrderSagaFulfillment", "1.0.0");
        await db.SaveChangesAsync();
        await service.AssignWorkflowClassAsync(TenantId, workflow.Id, app.Id);

        await service.AssignWorkflowClassAsync(TenantId, workflow.Id, null);

        Assert.Null((await db.WorkflowClasses.SingleAsync(w => w.Id == workflow.Id)).ApplicationId);
    }

    [Fact]
    public async Task Assign_refuses_another_tenants_application_and_workflow()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);
        var otherTenant = Guid.NewGuid();
        var foreignApp = await service.CreateAsync(otherTenant, "Theirs", null);
        var ownApp = await service.CreateAsync(TenantId, "Ours", null);
        var ownWorkflow = AddWorkflow(db, "Mine", "1.0.0");
        var foreignWorkflow = AddWorkflow(db, "Theirs", "1.0.0", otherTenant);
        await db.SaveChangesAsync();

        var appEx = await Assert.ThrowsAsync<TenantApplicationException>(
            () => service.AssignWorkflowClassAsync(TenantId, ownWorkflow.Id, foreignApp.Id));
        var workflowEx = await Assert.ThrowsAsync<TenantApplicationException>(
            () => service.AssignWorkflowClassAsync(TenantId, foreignWorkflow.Id, ownApp.Id));

        Assert.Equal(TenantApplicationErrorCodes.NotFound, appEx.Code);
        Assert.Equal(TenantApplicationErrorCodes.NotFound, workflowEx.Code);
        Assert.Null((await db.WorkflowClasses.SingleAsync(w => w.Id == foreignWorkflow.Id)).ApplicationId);
    }

    [Fact]
    public async Task Delete_is_refused_while_the_application_has_a_workflow()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);
        var app = await service.CreateAsync(TenantId, "Ops Incidents", null);
        var workflow = AddWorkflow(db, "IncidentAlertEscalation", "1.0.0");
        await db.SaveChangesAsync();
        await service.AssignWorkflowClassAsync(TenantId, workflow.Id, app.Id);

        var ex = await Assert.ThrowsAsync<TenantApplicationException>(() => service.DeleteAsync(TenantId, app.Id));

        Assert.Equal(TenantApplicationErrorCodes.InUse, ex.Code);
        Assert.Contains("workflow", ex.Message);

        await service.AssignWorkflowClassAsync(TenantId, workflow.Id, null);
        await service.DeleteAsync(TenantId, app.Id);
        Assert.Empty(await service.ListAsync(TenantId));
    }

    [Fact]
    public async Task List_counts_each_workflow_once_however_many_versions_it_has()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);
        var app = await service.CreateAsync(TenantId, "Finance Approvals", null);
        var v1 = AddWorkflow(db, "ExpenseApproval", "1.0.0");
        AddWorkflow(db, "ExpenseApproval", "2.0.0");
        var loan = AddWorkflow(db, "LoanUnderwritingFlow", "1.0.0");
        await db.SaveChangesAsync();
        await service.AssignWorkflowClassAsync(TenantId, v1.Id, app.Id);
        await service.AssignWorkflowClassAsync(TenantId, loan.Id, app.Id);

        var listed = Assert.Single(await service.ListAsync(TenantId));

        Assert.Equal(2, listed.WorkflowCount);
    }

    [Fact]
    public void A_new_version_stays_in_the_same_application()
    {
        var source = new WorkflowClass(TenantId, "ExpenseApproval", "1.0.0", new WorkflowClassBlueprint());
        var appId = Guid.NewGuid();
        source.AssignToApplication(appId);

        var next = new WorkflowClassVersionManager().CreateNewVersion(source, "1.1.0");

        Assert.Equal(appId, next.ApplicationId);
    }

    [Fact]
    public void A_copy_into_another_tenant_is_not_assigned_to_the_source_application()
    {
        var source = new WorkflowClass(TenantId, "ExpenseApproval", "1.0.0", new WorkflowClassBlueprint());
        source.AssignToApplication(Guid.NewGuid());

        var copy = new WorkflowClassVersionManager().CreateCopyForTenant(source, Guid.NewGuid());

        Assert.Null(copy.ApplicationId);
    }

    [Fact]
    public void Assigning_an_empty_guid_unassigns()
    {
        var workflow = new WorkflowClass(TenantId, "ExpenseApproval", "1.0.0", new WorkflowClassBlueprint());
        workflow.AssignToApplication(Guid.NewGuid());

        workflow.AssignToApplication(Guid.Empty);

        Assert.Null(workflow.ApplicationId);
    }
}
