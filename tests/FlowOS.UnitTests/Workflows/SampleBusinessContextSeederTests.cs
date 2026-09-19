using FlowOS.API.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class SampleBusinessContextSeederTests
{
    [Fact]
    public async Task SeedSampleBusinessContexts_AttachesRolesAndCapabilities_AndActivates()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"sample-context-{tenantId}")
            .Options);

        SeedExpenseTemplate(db, tenantId);
        SeedExpenseV2(db, tenantId);
        await DataSeeder.SeedFlagshipWorkflowsAsync(db, tenantId);
        await DataSeeder.SeedSampleBusinessContextsAsync(db, tenantId);

        var bindings = await db.WorkflowContextBindings
            .Where(binding => binding.TenantId == tenantId)
            .ToListAsync();
        Assert.Equal(5, bindings.Count);
        Assert.All(bindings, binding => Assert.Equal(WorkflowContextBindingStatus.Active, binding.Status));

        await AssertBindingVocabulary(db, tenantId, "Expense Approval Context", "Approver", "event.publish.EVT-APPROVE");
        await AssertBindingVocabulary(db, tenantId, "Expense Approval V2 Context", "Director", "event.publish.EVT-DIRECTOR-APPROVE");
        await AssertBindingVocabulary(db, tenantId, "Order Saga Context", "OrderClerk", "event.publish.EVT-VALIDATE");
        await AssertBindingVocabulary(db, tenantId, "Loan Underwriting Context", "Applicant", "event.publish.EVT-APPLY");
        await AssertBindingVocabulary(db, tenantId, "Privileged Access Context", "Requester", "event.publish.EVT-REQUEST-ACCESS");

        var expense = await db.WorkflowContextBindings.SingleAsync(binding =>
            binding.TenantId == tenantId && binding.Name == "Expense Approval Context");
        var expenseRevision = await db.WorkflowContextBindingRevisions.SingleAsync(revision =>
            revision.Id == expense.ActiveRevisionId);
        Assert.Equal("ExpenseEntity", expenseRevision.Definition.EntityType);
        Assert.Equal("amount", expenseRevision.Definition.InputMapping["Amount"]);

        var definition = await db.WorkflowDefinitions.SingleAsync(item =>
            item.Id == expenseRevision.WorkflowDefinitionId);
        Assert.Contains(definition.BusinessRoles, role => role.Name == "Approver");
        Assert.Contains(
            definition.BusinessRoles.Single(role => role.Name == "Approver").Capabilities,
            capability => capability == "event.publish.EVT-APPROVE");

        await DataSeeder.SeedSampleBusinessContextsAsync(db, tenantId);
        Assert.Equal(5, await db.WorkflowContextBindings.CountAsync(binding => binding.TenantId == tenantId));
    }

    [Fact]
    public async Task SeedSampleBusinessContexts_BackfillsRolesOnExistingEmptyBinding()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"sample-context-backfill-{tenantId}")
            .Options);

        SeedExpenseTemplate(db, tenantId);
        var workflowClass = await db.WorkflowClasses.SingleAsync(item =>
            item.TenantId == tenantId && item.Name == "ExpenseApproval");

        var binding = new WorkflowContextBinding(tenantId, "Expense", "Expense Approval Context");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            workflowClass.Id,
            workflowClass.Version,
            new FlowOS.Domain.ValueObjects.WorkflowContextBindingDefinition
            {
                EntityType = "ExpenseEntity"
            });
        binding.SetDraftRevision(revision.Id);
        db.WorkflowContextBindings.Add(binding);
        db.WorkflowContextBindingRevisions.Add(revision);
        await db.SaveChangesAsync();

        await DataSeeder.SeedSampleBusinessContextsAsync(db, tenantId);

        var updated = await db.WorkflowContextBindingRevisions.SingleAsync(item => item.Id == revision.Id);
        Assert.Contains("Approver", updated.Definition.RoleOverrides.Keys);
        Assert.Contains("event.publish.EVT-APPROVE", updated.Definition.CapabilityOverrides.Keys);
        Assert.Equal(1, await db.WorkflowContextBindings.CountAsync(item => item.TenantId == tenantId));
    }

    private static async Task AssertBindingVocabulary(
        FlowOSDbContext db,
        Guid tenantId,
        string bindingName,
        string expectedRole,
        string expectedCapability)
    {
        var binding = await db.WorkflowContextBindings.SingleAsync(item =>
            item.TenantId == tenantId && item.Name == bindingName);
        var revision = await db.WorkflowContextBindingRevisions.SingleAsync(item =>
            item.Id == binding.ActiveRevisionId);
        Assert.Contains(expectedRole, revision.Definition.RoleOverrides.Keys);
        Assert.Contains(expectedCapability, revision.Definition.CapabilityOverrides.Keys);

        var definition = await db.WorkflowDefinitions.SingleAsync(item =>
            item.Id == revision.WorkflowDefinitionId);
        Assert.Contains(definition.BusinessRoles, role => role.Name == expectedRole);
        Assert.Contains(
            definition.BusinessRoles.Single(role => role.Name == expectedRole).Capabilities,
            capability => capability == expectedCapability);
    }

    private static void SeedExpenseTemplate(FlowOSDbContext db, Guid tenantId)
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events =
            [
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request" },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request" },
                new EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request" }
            ],
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = ["Draft", "Pending", "Approved", "Rejected"],
                Transitions =
                [
                    new TransitionBlueprint { FromState = "Draft", ToState = "Pending", EventId = "EVT-SUBMIT" },
                    new TransitionBlueprint { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" },
                    new TransitionBlueprint { FromState = "Pending", ToState = "Rejected", EventId = "EVT-REJECT" }
                ]
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Draft",
                Steps =
                [
                    new StepBlueprint { StepId = "Draft", StepType = "Command", NextSteps = new() { ["EVT-SUBMIT"] = "Pending" } },
                    new StepBlueprint
                    {
                        StepId = "Pending",
                        StepType = "HumanTask",
                        RequiredRoles = ["Approver"],
                        NextSteps = new() { ["EVT-APPROVE"] = "Approved", ["EVT-REJECT"] = "Rejected" }
                    },
                    new StepBlueprint { StepId = "Approved", StepType = "Command", NextSteps = new() { ["Default"] = "END" } },
                    new StepBlueprint { StepId = "Rejected", StepType = "Command", NextSteps = new() { ["Default"] = "END" } }
                ]
            },
            Roles =
            [
                new RoleBlueprint { Name = "Approver", GrantedCapabilities = ["event.publish.EVT-APPROVE", "event.publish.EVT-REJECT"] }
            ],
            Capabilities =
            [
                new CapabilityBlueprint { Code = "event.publish.EVT-APPROVE" },
                new CapabilityBlueprint { Code = "event.publish.EVT-REJECT" }
            ]
        };
        PublishClass(db, tenantId, "ExpenseApproval", blueprint);
    }

    private static void SeedExpenseV2(FlowOSDbContext db, Guid tenantId)
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events =
            [
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request", AllowedRoles = ["Employee"] },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request", AllowedRoles = ["Manager"] },
                new EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request", AllowedRoles = ["Manager"] },
                new EventBlueprint { EventId = "EVT-ESCALATE", Name = "Escalate", AllowedRoles = ["Manager"] },
                new EventBlueprint { EventId = "EVT-DIRECTOR-APPROVE", Name = "Director Approve", AllowedRoles = ["Director"] },
                new EventBlueprint { EventId = "EVT-DIRECTOR-REJECT", Name = "Director Reject", AllowedRoles = ["Director"] }
            ],
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = ["Draft", "PendingManager", "PendingDirector", "Approved", "Rejected"],
                Transitions =
                [
                    new TransitionBlueprint { FromState = "Draft", ToState = "PendingManager", EventId = "EVT-SUBMIT" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Approved", EventId = "EVT-APPROVE" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "PendingDirector", EventId = "EVT-ESCALATE" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Rejected", EventId = "EVT-REJECT" },
                    new TransitionBlueprint { FromState = "PendingDirector", ToState = "Approved", EventId = "EVT-DIRECTOR-APPROVE" },
                    new TransitionBlueprint { FromState = "PendingDirector", ToState = "Rejected", EventId = "EVT-DIRECTOR-REJECT" }
                ]
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Draft",
                Steps =
                [
                    new StepBlueprint { StepId = "Draft", StepType = "Command", NextSteps = new() { ["EVT-SUBMIT"] = "PendingManager" } },
                    new StepBlueprint
                    {
                        StepId = "PendingManager",
                        StepType = "HumanTask",
                        RequiredRoles = ["Manager"],
                        NextSteps = new() { ["EVT-APPROVE"] = "Approved", ["EVT-ESCALATE"] = "PendingDirector", ["EVT-REJECT"] = "Rejected" }
                    },
                    new StepBlueprint
                    {
                        StepId = "PendingDirector",
                        StepType = "HumanTask",
                        RequiredRoles = ["Director"],
                        NextSteps = new() { ["EVT-DIRECTOR-APPROVE"] = "Approved", ["EVT-DIRECTOR-REJECT"] = "Rejected" }
                    },
                    new StepBlueprint { StepId = "Approved", StepType = "Command", NextSteps = new() { ["Default"] = "END" } },
                    new StepBlueprint { StepId = "Rejected", StepType = "Command", NextSteps = new() { ["Default"] = "END" } }
                ]
            },
            Roles =
            [
                new RoleBlueprint { Name = "Manager", GrantedCapabilities = ["event.publish.EVT-APPROVE", "event.publish.EVT-REJECT", "event.publish.EVT-ESCALATE"] },
                new RoleBlueprint { Name = "Director", GrantedCapabilities = ["event.publish.EVT-DIRECTOR-APPROVE", "event.publish.EVT-DIRECTOR-REJECT"] }
            ],
            Capabilities =
            [
                new CapabilityBlueprint { Code = "event.publish.EVT-APPROVE" },
                new CapabilityBlueprint { Code = "event.publish.EVT-REJECT" },
                new CapabilityBlueprint { Code = "event.publish.EVT-ESCALATE" },
                new CapabilityBlueprint { Code = "event.publish.EVT-DIRECTOR-APPROVE" },
                new CapabilityBlueprint { Code = "event.publish.EVT-DIRECTOR-REJECT" }
            ]
        };
        PublishClass(db, tenantId, "ExpenseApprovalV2", blueprint);
    }

    private static void PublishClass(FlowOSDbContext db, Guid tenantId, string name, WorkflowClassBlueprint blueprint)
    {
        WorkflowSimulationGovernance.Apply(blueprint);
        var workflowClass = new WorkflowClass(tenantId, name, "1.0.0", blueprint);
        Assert.True(new WorkflowClassManager().Publish(workflowClass).IsValid);
        db.WorkflowClasses.Add(workflowClass);
        db.SaveChanges();
    }
}
