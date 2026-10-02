using FlowOS.Domain.Entities;
using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.Domain.Enums;
using Xunit;

namespace FlowOS.UnitTests;

public class Task12EntityTests
{
    [Fact]
    public void Tenant_NewTenant_ExternalAIFieldsDefaultNull()
    {
        var tenant = new Tenant("Test Tenant");

        Assert.Null(tenant.ExternalAIAgentEnabled);
        Assert.Null(tenant.ExternalAIAgentAutoPilot);
        Assert.Null(tenant.ExternalAIAgentProfileId);
    }

    [Fact]
    public void Tenant_SetExternalAIAgent_SetsPropertiesAndUpdatedAt()
    {
        var tenant = new Tenant("Test Tenant");

        tenant.SetExternalAIAgent(true, true, "profile-123");

        Assert.True(tenant.ExternalAIAgentEnabled);
        Assert.True(tenant.ExternalAIAgentAutoPilot);
        Assert.Equal("profile-123", tenant.ExternalAIAgentProfileId);
        Assert.NotNull(tenant.UpdatedAt);
    }

    [Fact]
    public void Tenant_SetExternalAIAgent_TrimsAndNullifiesEmptyProfile()
    {
        var tenant = new Tenant("Test Tenant");

        tenant.SetExternalAIAgent(false, false, "   ");

        Assert.False(tenant.ExternalAIAgentEnabled);
        Assert.False(tenant.ExternalAIAgentAutoPilot);
        Assert.Null(tenant.ExternalAIAgentProfileId);
    }

    [Fact]
    public void ExternalAgentChangeRecord_FromOutboxMessage_CreatesPendingRecord()
    {
        var tenantId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var change = ExternalAgentChangeRecord.FromOutboxMessage(
            id, tenantId, outboxId, "EVT_TEST", "{\"key\":\"val\"}", now);

        Assert.Equal(id, change.Id);
        Assert.Equal(tenantId, change.TenantId);
        Assert.Equal(outboxId, change.SourceOutboxMessageId);
        Assert.Equal("EVT_TEST", change.EventType);
        Assert.Equal("{\"key\":\"val\"}", change.PayloadJson);
        Assert.Equal(ExternalAgentChangeStatus.Pending, change.Status);
        Assert.Equal(0, change.AttemptCount);
        Assert.Equal(5, change.MaxAttempts);
        Assert.Equal(now, change.CreatedAtUtc);
    }

    [Fact]
    public void ExternalAgentChangeRecord_MarkLeased_PendingTransitions()
    {
        var change = CreatePendingChange();
        var agentId = "agent-1";
        var ttl = TimeSpan.FromMinutes(5);

        change.MarkLeased(agentId, ttl);

        Assert.Equal(ExternalAgentChangeStatus.Leased, change.Status);
        Assert.Equal(agentId, change.LeasedByAgent);
        Assert.NotNull(change.LeasedUntilUtc);
        Assert.True(change.LeasedUntilUtc > DateTime.UtcNow);
    }

    [Fact]
    public void ExternalAgentChangeRecord_MarkProcessed_FromLeased_Succeeds()
    {
        var change = CreatePendingChange();
        change.MarkLeased("agent-1", TimeSpan.FromMinutes(5));

        change.MarkProcessed();

        Assert.Equal(ExternalAgentChangeStatus.Processed, change.Status);
        Assert.Null(change.LeasedByAgent);
        Assert.NotNull(change.ProcessedAtUtc);
    }

    [Fact]
    public void ExternalAgentChangeRecord_MarkFailed_ExponentialBackoff()
    {
        var change = CreatePendingChange();
        change.MarkLeased("agent-1", TimeSpan.FromMinutes(5));
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        change.MarkFailed("error 1", now);

        Assert.Equal(ExternalAgentChangeStatus.Failed, change.Status);
        Assert.Equal(1, change.AttemptCount);
        Assert.NotNull(change.NextRetryUtc);
        Assert.Equal("error 1", change.LastError);
    }

    [Fact]
    public void ExternalAgentChangeRecord_MarkDeadLetter_AfterMaxAttempts()
    {
        var change = CreatePendingChange();
        for (int i = 0; i < 4; i++)
        {
            change.MarkLeased("a", TimeSpan.FromMinutes(1));
            change.MarkFailed("err", DateTime.UtcNow);
        }
        change.MarkLeased("a", TimeSpan.FromMinutes(1));

        change.MarkFailed("final", DateTime.UtcNow);

        Assert.Equal(ExternalAgentChangeStatus.DeadLetter, change.Status);
        Assert.Equal(5, change.AttemptCount);
        Assert.Null(change.LeasedByAgent);
        Assert.Null(change.LeasedUntilUtc);
        Assert.Null(change.NextRetryUtc);
    }

    [Fact]
    public void ExternalAgentChangeRecord_ComputeNextRetryUtc_ExponentialGrowth()
    {
        var change = CreatePendingChange();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var r1 = change.ComputeNextRetryUtc(1, now);
        var r2 = change.ComputeNextRetryUtc(2, now);
        var r3 = change.ComputeNextRetryUtc(3, now);

        Assert.Equal(now.AddSeconds(2), r1);
        Assert.Equal(now.AddSeconds(4), r2);
        Assert.Equal(now.AddSeconds(8), r3);
    }

    [Fact]
    public void ExternalAgentChangeRecord_ReclaimLease_RevertsToPending()
    {
        var change = CreatePendingChange();
        change.MarkLeased("agent-x", TimeSpan.FromMinutes(1));

        change.ReclaimLease();

        Assert.Equal(ExternalAgentChangeStatus.Pending, change.Status);
        Assert.Null(change.LeasedByAgent);
        Assert.Null(change.LeasedUntilUtc);
    }

    [Fact]
    public void ExternalAgentPlanRecord_Create_ImmutableValues()
    {
        var id = Guid.NewGuid();
        var changeId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var plan = ExternalAgentPlanRecord.Create(
            id, changeId, tenantId, "prof", now, "[{}]", 2);

        Assert.Equal(id, plan.Id);
        Assert.Equal(changeId, plan.ChangeId);
        Assert.Equal(tenantId, plan.TenantId);
        Assert.Equal("prof", plan.AgentProfileId);
        Assert.Equal(now, plan.CreatedAtUtc);
        Assert.Equal("[{}]", plan.PlanStepsJson);
        Assert.Equal(2, plan.PlanVersion);
    }

    [Fact]
    public void ExternalAgentPlanStepRecord_Lifecycle_Transitions()
    {
        var step = ExternalAgentPlanStepRecord.Create(
            1L, Guid.NewGuid(), "step-1", 0, "tool_a", "{}", "desc", "[]");
        var start = DateTime.UtcNow;
        var finish = start.AddSeconds(3);

        Assert.Equal(ExternalAgentStepStatus.Pending, step.Status);

        step.MarkRunning(start);
        Assert.Equal(ExternalAgentStepStatus.Running, step.Status);
        Assert.Equal(start, step.StartedAtUtc);

        step.MarkSucceeded(finish, "{\"ok\":true}");
        Assert.Equal(ExternalAgentStepStatus.Succeeded, step.Status);
        Assert.Equal(finish, step.FinishedAtUtc);
        Assert.Equal("{\"ok\":true}", step.ResultSnapshotJson);
    }

    [Fact]
    public void ExternalAgentPlanStepRecord_MarkFailed_SetsError()
    {
        var step = ExternalAgentPlanStepRecord.Create(
            1L, Guid.NewGuid(), "s1", 0, "t", "{}", null, "[]");
        var start = DateTime.UtcNow;

        step.MarkRunning(start);
        step.MarkFailed(start.AddSeconds(2), "boom");

        Assert.Equal(ExternalAgentStepStatus.Failed, step.Status);
        Assert.Equal("boom", step.ErrorMessage);
    }

    [Fact]
    public void ExternalAgentPlanStepRecord_MarkSkipped_PendingOrFailedOnly()
    {
        var step = ExternalAgentPlanStepRecord.Create(
            1L, Guid.NewGuid(), "s1", 0, "t", "{}", null, "[]");

        step.MarkSkipped();
        Assert.Equal(ExternalAgentStepStatus.Skipped, step.Status);

        step.ResetToPending();
        Assert.Equal(ExternalAgentStepStatus.Pending, step.Status);
    }

    private static ExternalAgentChangeRecord CreatePendingChange()
    {
        return ExternalAgentChangeRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "EVT_X",
            "{}",
            DateTime.UtcNow);
    }
}
