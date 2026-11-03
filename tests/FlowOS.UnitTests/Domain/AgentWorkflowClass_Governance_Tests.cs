using System;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities.Orchestration;
using FlowOS.Domain.Enums;
using Xunit;

namespace FlowOS.UnitTests.Domain;

/// <summary>
/// ADR-006 Phase 4A: AgentWorkflowClass lifecycle and governance rule tests.
/// Mirrors WorkflowClass_Governance_Tests.cs.
/// </summary>
public class AgentWorkflowClass_Governance_Tests
{
    private static readonly DateTime FixedNow = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static AgentWorkflowClass CreateValidDraft(Guid? tenantId = null) =>
        new AgentWorkflowClass(
            tenantId ?? Guid.NewGuid(),
            "Quarterly Risk Assessment Plan",
            "1.0.0",
            new AgentWorkflowBlueprint
            {
                Objective = "Assess quarterly risk across all teams",
                StartStepId = "start",
                Steps =
                [
                    new AgentStepBlueprint
                    {
                        StepId = "start",
                        Description = "Gather risk data",
                        NextSteps = { { "Done", "end" } }
                    },
                    new AgentStepBlueprint
                    {
                        StepId = "end",
                        Description = "Summarise and report"
                    }
                ]
            },
            FixedNow);

    // ── Constructor ─────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_Sets_CreatedAt_As_Utc()
    {
        var localTime = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Local);
        var plan = new AgentWorkflowClass(Guid.NewGuid(), "P", "1.0", new AgentWorkflowBlueprint(), localTime);
        Assert.Equal(DateTimeKind.Utc, plan.CreatedAt.Kind);
    }

    [Fact]
    public void Constructor_DefaultStatus_Is_Draft()
    {
        var plan = CreateValidDraft();
        Assert.Equal(WorkflowClassStatus.Draft, plan.Status);
    }

    [Fact]
    public void Constructor_NullDefinition_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentWorkflowClass(Guid.NewGuid(), "P", "1.0", null!, FixedNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new AgentWorkflowClass(Guid.NewGuid(), name, "1.0", new AgentWorkflowBlueprint(), FixedNow));
    }

    // ── Publish ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Publish_Draft_Succeeds()
    {
        var plan = CreateValidDraft();
        plan.Publish(FixedNow.AddSeconds(1));
        Assert.Equal(WorkflowClassStatus.Published, plan.Status);
        Assert.NotNull(plan.PublishedAt);
    }

    [Fact]
    public void Publish_Published_Throws()
    {
        var plan = CreateValidDraft();
        plan.Publish(FixedNow);
        var ex = Assert.Throws<InvalidOperationException>(() => plan.Publish(FixedNow.AddSeconds(1)));
        Assert.Contains("Only Draft plans", ex.Message);
    }

    // ── UpdateDraft ─────────────────────────────────────────────────────────────

    [Fact]
    public void UpdateDraft_Draft_Succeeds()
    {
        var plan = CreateValidDraft();
        var newBlueprint = new AgentWorkflowBlueprint { Objective = "Updated" };
        plan.UpdateDraft("New Name", "1.1.0", newBlueprint, "Fixed a bug");
        Assert.Equal("New Name", plan.Name);
        Assert.Equal("1.1.0", plan.Version);
        Assert.Equal("Updated", plan.Definition.Objective);
        Assert.Equal("Fixed a bug", plan.ChangeLog);
    }

    [Fact]
    public void UpdateDraft_Published_Throws()
    {
        var plan = CreateValidDraft();
        plan.Publish(FixedNow);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            plan.UpdateDraft("X", "2.0.0", new AgentWorkflowBlueprint()));
        Assert.Contains("Only Draft plans", ex.Message);
    }

    // ── Delete (GOV-DEL-001 / GOV-DEL-002) ─────────────────────────────────────

    [Fact]
    public void Delete_Draft_NoExecutions_Succeeds()
    {
        var plan = CreateValidDraft();
        // Should not throw
        plan.Delete(hasExecutions: false);
    }

    [Fact]
    public void Delete_Draft_WithExecutions_Throws_GOV_DEL_002()
    {
        var plan = CreateValidDraft();
        var ex = Assert.Throws<InvalidOperationException>(() => plan.Delete(hasExecutions: true));
        Assert.Contains("GOV-DEL-002", ex.Message);
    }

    [Fact]
    public void Delete_Published_Throws_GOV_DEL_001()
    {
        var plan = CreateValidDraft();
        plan.Publish(FixedNow);
        var ex = Assert.Throws<InvalidOperationException>(() => plan.Delete(hasExecutions: false));
        Assert.Contains("GOV-DEL-001", ex.Message);
    }

    // ── Deprecate ───────────────────────────────────────────────────────────────

    [Fact]
    public void Deprecate_Published_Succeeds()
    {
        var plan = CreateValidDraft();
        plan.Publish(FixedNow);
        var migrationTarget = Guid.NewGuid();
        plan.Deprecate("Moving to v2", migrationTarget);
        Assert.Equal(WorkflowClassStatus.Deprecated, plan.Status);
        Assert.Equal("Moving to v2", plan.DeprecationReason);
        Assert.Equal(migrationTarget, plan.DeprecationMigrationTargetId);
    }

    [Fact]
    public void Deprecate_Draft_Throws()
    {
        var plan = CreateValidDraft();
        var ex = Assert.Throws<InvalidOperationException>(() => plan.Deprecate(null, null));
        Assert.Contains("Only Published plans", ex.Message);
    }

    // ── Abandon ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Abandon_Published_Succeeds()
    {
        var plan = CreateValidDraft();
        plan.Publish(FixedNow);
        plan.Abandon();
        Assert.Equal(WorkflowClassStatus.Abandoned, plan.Status);
    }

    [Fact]
    public void Abandon_Deprecated_Succeeds()
    {
        var plan = CreateValidDraft();
        plan.Publish(FixedNow);
        plan.Deprecate(null, null);
        plan.Abandon();
        Assert.Equal(WorkflowClassStatus.Abandoned, plan.Status);
    }

    [Fact]
    public void Abandon_Abandoned_IsIdempotent()
    {
        var plan = CreateValidDraft();
        plan.Publish(FixedNow);
        plan.Abandon();
        // Should not throw
        plan.Abandon();
        Assert.Equal(WorkflowClassStatus.Abandoned, plan.Status);
    }

    [Fact]
    public void Abandon_Draft_Throws()
    {
        var plan = CreateValidDraft();
        var ex = Assert.Throws<InvalidOperationException>(() => plan.Abandon());
        Assert.Contains("Cannot abandon a Draft", ex.Message);
    }

    // ── Versioning / lineage ────────────────────────────────────────────────────

    [Fact]
    public void Constructor_PreviousVersionId_IsSet_When_Provided()
    {
        var prevId = Guid.NewGuid();
        var plan = new AgentWorkflowClass(
            Guid.NewGuid(), "Plan v2", "2.0.0", new AgentWorkflowBlueprint(), FixedNow,
            previousVersionId: prevId, changeLog: "Rewrote step graph");

        Assert.Equal(prevId, plan.PreviousVersionId);
        Assert.Equal("Rewrote step graph", plan.ChangeLog);
    }

    [Fact]
    public void NewPlan_HasNoPreviousVersion_ByDefault()
    {
        var plan = CreateValidDraft();
        Assert.Null(plan.PreviousVersionId);
    }
}
