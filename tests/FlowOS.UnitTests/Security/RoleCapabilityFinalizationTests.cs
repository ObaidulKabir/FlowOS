using System.Security.Claims;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Domain.ValueObjects;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Infrastructure.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlowOS.UnitTests.Security;

public class RoleCapabilityFinalizationTests
{
    [Fact]
    public void Resolve_UnmappedTemplateRole_CarriesDeclaredCapabilities()
    {
        var required = ContextRoleProvisioningRules.Resolve(
            Blueprint(),
            new WorkflowContextBindingDefinition { EntityType = "Expense" });

        var approver = required.Single(x => x.RoleName == "Approver");
        Assert.False(approver.IsExplicitlyMapped);
        Assert.Equal(["event.publish.EVT-APPROVE"], approver.Capabilities);
        Assert.Equal("Assignment", approver.ResolutionType);
    }

    [Fact]
    public void Resolve_AppliesRoleAndCapabilityOverrides()
    {
        var required = ContextRoleProvisioningRules.Resolve(
            Blueprint(),
            new WorkflowContextBindingDefinition
            {
                EntityType = "Expense",
                RoleOverrides = new Dictionary<string, string> { ["Approver"] = "FinanceManager" },
                CapabilityOverrides = new Dictionary<string, string>
                {
                    ["event.publish.EVT-APPROVE"] = "event.publish.EVT-EXP-APPROVE"
                }
            });

        var mapped = required.Single(x => x.RoleName == "FinanceManager");
        Assert.True(mapped.IsExplicitlyMapped);
        Assert.Equal(["event.publish.EVT-EXP-APPROVE"], mapped.Capabilities);
    }

    [Fact]
    public void Resolve_MergesTemplateRolesThatShareOneTenantRole()
    {
        var required = ContextRoleProvisioningRules.Resolve(
            Blueprint(),
            new WorkflowContextBindingDefinition
            {
                EntityType = "Expense",
                RoleOverrides = new Dictionary<string, string>
                {
                    ["Approver"] = "FinanceManager",
                    ["Auditor"] = "FinanceManager"
                }
            });

        var merged = Assert.Single(required);
        Assert.Equal("FinanceManager", merged.RoleName);
        Assert.Equal(["Approver", "Auditor"], merged.TemplateRoles);
        Assert.Equal(["event.publish.EVT-APPROVE", "workflow.read"], merged.Capabilities);
    }

    [Fact]
    public async Task Activation_CompilesBusinessRolesButNeverTouchesFlowOSTenantRoles()
    {
        // This is the regression test for the fix: a business-context role declared on the
        // WorkflowClass (e.g. "Approver") must never be written into FlowOS's own Role/
        // TenantUserRole tables — it only rides along as declarative metadata on the compiled
        // WorkflowDefinition, resolved per instance by IBusinessRoleResolver at runtime.
        var source = Source();
        Assert.True(new WorkflowClassManager().Publish(source).IsValid);

        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition { EntityType = "ExpenseEntity" });
        binding.SetDraftRevision(revision.Id);

        await using var context = NewContext();
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        await context.SaveChangesAsync();

        var package = await Materializer(context).ActivateAsync(binding, revision);

        var approver = Assert.Single(package.WorkflowDefinition.BusinessRoles);
        Assert.Equal("Approver", approver.Name);
        Assert.Contains("event.publish.EVT-APPROVE", approver.Capabilities);
        Assert.Equal("Assignment", approver.ResolutionType);

        // Activation must not have created (or touched) any row in FlowOS's own Roles table.
        Assert.False(await context.Roles.AnyAsync(r => r.TenantId == source.TenantId));
    }

    [Fact]
    public async Task Activation_LeavesAPreExistingSameNamedFlowOSRoleCompletelyUntouched()
    {
        // A FlowOS tenant admin role that happens to share a name with a business-context role
        // ("Approver") is a coincidence, not a relationship — activation must not read or write it.
        var source = Source();
        Assert.True(new WorkflowClassManager().Publish(source).IsValid);

        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition { EntityType = "ExpenseEntity" });
        binding.SetDraftRevision(revision.Id);

        var existing = new FlowOS.Security.Models.Role(source.TenantId, "Approver");
        existing.AddPermission("workflow.read");

        await using var context = NewContext();
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        context.Roles.Add(existing);
        await context.SaveChangesAsync();

        await Materializer(context).ActivateAsync(binding, revision);

        var role = await context.Roles
            .SingleAsync(r => r.TenantId == source.TenantId && r.Name == "Approver");
        Assert.Equal(["workflow.read"], role.Permissions);
        Assert.DoesNotContain("event.publish.EVT-APPROVE", role.Permissions);
    }

    [Fact]
    public async Task Activation_AllowsRoleOverrideOntoAnyName_NoFlowOSRoleRequiredToExist()
    {
        // Business-context RoleOverrides are just a rename within the business vocabulary now —
        // there is no FlowOS tenant role for "TypoRole" to match against, so this must succeed.
        var source = Source();
        Assert.True(new WorkflowClassManager().Publish(source).IsValid);

        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition
            {
                EntityType = "ExpenseEntity",
                RoleOverrides = new Dictionary<string, string> { ["Approver"] = "RegionalManager" }
            });
        binding.SetDraftRevision(revision.Id);

        await using var context = NewContext();
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        await context.SaveChangesAsync();

        var package = await Materializer(context).ActivateAsync(binding, revision);

        var mapped = Assert.Single(package.WorkflowDefinition.BusinessRoles);
        Assert.Equal("RegionalManager", mapped.Name);
        Assert.False(await context.Roles.AnyAsync(r => r.TenantId == source.TenantId));
    }

    [Fact]
    public async Task Activation_RejectsRoleOverrideThatReferencesUndeclaredTemplateRole()
    {
        var source = Source();
        Assert.True(new WorkflowClassManager().Publish(source).IsValid);

        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition
            {
                EntityType = "ExpenseEntity",
                RoleOverrides = new Dictionary<string, string> { ["NoSuchTemplateRole"] = "RegionalManager" }
            });
        binding.SetDraftRevision(revision.Id);

        await using var context = NewContext();
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<FlowOS.Application.Common.Exceptions.WorkflowContextBindingValidationException>(
            () => Materializer(context).ActivateAsync(binding, revision));
    }

    [Fact]
    public async Task AssignedRoles_AreListedForUserAndScopedToTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var context = NewContext();
        var advisor = new FlowOS.Security.Models.Role(tenantId, "Advisor");
        var approver = new FlowOS.Security.Models.Role(tenantId, "Approver");
        var foreign = new FlowOS.Security.Models.Role(otherTenantId, "Outsider");
        context.Roles.AddRange(advisor, approver, foreign);
        await context.SaveChangesAsync();

        var repository = new RoleRepository(context);
        Assert.True(await repository.AssignToUserAsync(tenantId, userId, advisor.Id));
        Assert.True(await repository.AssignToUserAsync(tenantId, userId, approver.Id));
        Assert.False(await repository.AssignToUserAsync(tenantId, userId, advisor.Id));
        Assert.True(await repository.AssignToUserAsync(otherTenantId, userId, foreign.Id));
        await context.SaveChangesAsync();

        Assert.Equal(["Advisor", "Approver"], await repository.ListAssignedRoleNamesAsync(tenantId, userId));

        Assert.True(await repository.RevokeFromUserAsync(tenantId, userId, advisor.Id));
        await context.SaveChangesAsync();
        Assert.Equal(["Approver"], await repository.ListAssignedRoleNamesAsync(tenantId, userId));
    }

    [Fact]
    public void Token_CarriesPrimaryAndAssignedRolesWithoutDuplicates()
    {
        var service = new JwtTokenService(
            new ConfigurationBuilder().Build(),
            NullLogger<JwtTokenService>.Instance);

        var token = service.GenerateToken(
            Guid.NewGuid(),
            "user@example.com",
            "Test User",
            Guid.NewGuid(),
            "Acme",
            "Advisor",
            ["Approver", "advisor"]);

        var principal = service.ValidateToken(token);

        Assert.NotNull(principal);
        var roles = principal!.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Equal(["Advisor", "Approver"], roles);
        Assert.True(principal.IsInRole("Approver"));
    }

    [Fact]
    public void Compiler_NormalizesInvokeConnectorOntoStoredCapabilityShape()
    {
        var source = Source();
        source.Definition.Workflow.Steps.Single().OnEntry.Add(new StepActionBlueprint
        {
            ActionType = "InvokeConnector",
            Connector = "payment.refund.v1"
        });

        var definition = WorkflowClassCompiler.MapToRuntimeDefinition(source);
        var action = definition.Steps.Single().OnEntry.Single();

        Assert.Equal("InvokeCapability", action.ActionType);
        Assert.Equal("payment.refund.v1", action.Capability);
    }

    [Fact]
    public void Validator_AcceptsInvokeConnector_AndStillRequiresAConnectorName()
    {
        var withConnector = Source();
        withConnector.Definition.Workflow.Steps.Single().OnEntry.Add(new StepActionBlueprint
        {
            ActionType = "InvokeConnector",
            Connector = "payment.refund.v1"
        });
        withConnector.Definition.Workflow.Steps.Single().OnFailure.Add(new StepActionBlueprint
        {
            ActionType = "Notification",
            Target = "Approver"
        });
        Assert.True(new WorkflowClassManager().Publish(withConnector).IsValid);

        var missingName = Source();
        missingName.Definition.Workflow.Steps.Single().OnEntry.Add(new StepActionBlueprint
        {
            ActionType = "InvokeConnector"
        });
        var result = new WorkflowClassManager().Publish(missingName);

        Assert.Contains(result.Errors, e => e.Code == "WF-ACT-005");
    }

    [Fact]
    public void AgentTools_TreatConnectorPrefixLikeCapabilityPrefix()
    {
        var connector = AgentToolCatalog.Parse("connector:payment.refund.v1");
        var capability = AgentToolCatalog.Parse("capability:payment.refund.v1");

        Assert.Equal(capability.Kind, connector.Kind);
        Assert.Equal("payment.refund.v1", connector.Capability);
        Assert.Equal("write", connector.SideEffect);
        Assert.False(connector.Prefetch);
    }

    private static FlowOSDbContext NewContext()
        => new(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static WorkflowContextMaterializer Materializer(FlowOSDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        return new WorkflowContextMaterializer(
            unitOfWork,
            new WorkflowContextBindingValidator(
                unitOfWork,
                new Mock<IPolicyDecisionPluginRegistry>().Object));
    }

    private static WorkflowClassBlueprint Blueprint() => new()
    {
        Roles =
        [
            new RoleBlueprint { Name = "Approver", GrantedCapabilities = ["event.publish.EVT-APPROVE"] },
            new RoleBlueprint { Name = "Auditor", GrantedCapabilities = ["workflow.read"] }
        ],
        Capabilities =
        [
            new CapabilityBlueprint { Code = "event.publish.EVT-APPROVE" },
            new CapabilityBlueprint { Code = "workflow.read" }
        ]
    };

    private static WorkflowClass Source() => new(
        Guid.NewGuid(),
        "ReusableApproval",
        "1.0.0",
        new WorkflowClassBlueprint
        {
            Events = [new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve" }],
            StateMachine = new StateMachineBlueprint
            {
                EntityType = "ApprovalSubject",
                InitialState = "Pending",
                States = ["Pending", "Approved"],
                Transitions =
                [
                    new TransitionBlueprint
                    {
                        FromState = "Pending",
                        ToState = "Approved",
                        EventId = "EVT-APPROVE"
                    }
                ]
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Review",
                Steps =
                [
                    new StepBlueprint
                    {
                        StepId = "Review",
                        StepType = "HumanTask",
                        RequiredRoles = ["Approver"],
                        NextSteps = new Dictionary<string, string> { ["EVT-APPROVE"] = "END" }
                    }
                ]
            },
            Roles =
            [
                new RoleBlueprint { Name = "Approver", GrantedCapabilities = ["event.publish.EVT-APPROVE"] }
            ],
            Capabilities = [new CapabilityBlueprint { Code = "event.publish.EVT-APPROVE" }]
        });
}
