using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowTimeTravelServiceTests
{
    [Fact]
    public async Task GetReplayTimeline_ReconstructsChronologicalSnapshotsAndVariables()
    {
        var fixture = await SeedLoanApprovalAsync();

        var replay = await fixture.Service.GetReplayTimelineAsync(fixture.TenantId, fixture.Instance.Id);

        Assert.NotNull(replay);
        Assert.Equal(2, replay!.TotalSteps);
        Assert.Equal("LoanApproval", replay.WorkflowClassName);
        Assert.Equal(fixture.Instance.Id, replay.WorkflowInstanceId);
        Assert.Equal("Start", replay.Snapshots[0].ToStepId);
        Assert.Equal("Pending", replay.Snapshots[1].ToStepId);
        Assert.Equal("Pending", replay.Snapshots[1].ToState);
        Assert.Equal("5000", replay.Snapshots[1].Variables["amount"]?.ToString());
        Assert.Contains(replay.Snapshots[1].ActionLogs, a => a.ActionType == "Webhook" && a.StepId == "Pending");
    }

    [Fact]
    public async Task SimulateFork_ProjectsAlternativeRejectPathWithoutMutatingLiveInstance()
    {
        var fixture = await SeedLoanApprovalAsync();
        var originalStep = fixture.Instance.CurrentStepId;

        var result = await fixture.Service.SimulateForkAsync(
            fixture.TenantId,
            fixture.Instance.Id,
            targetStepIndex: 1,
            alternativeEvent: "EVT-REJECT");

        Assert.True(result.IsAllowed, result.Reason);
        Assert.Equal("Pending", result.BaseStepId);
        Assert.Equal("Rejected", result.ProjectedStepId);
        Assert.Equal("Rejected", result.ProjectedState);
        Assert.Contains(result.ProjectedActions, a => a.Contains("OnExit(Pending)"));

        var live = await fixture.Db.WorkflowInstances.AsNoTracking().FirstAsync(i => i.Id == fixture.Instance.Id);
        Assert.Equal(originalStep, live.CurrentStepId);
        Assert.Equal(WorkflowInstanceStatus.Running, live.Status);
        Assert.Equal(2, await fixture.Db.Events.CountAsync());
        Assert.Empty(await fixture.Db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task SimulateFork_RejectsUnknownEventTransition()
    {
        var fixture = await SeedLoanApprovalAsync();

        var result = await fixture.Service.SimulateForkAsync(
            fixture.TenantId,
            fixture.Instance.Id,
            targetStepIndex: 1,
            alternativeEvent: "EVT-UNKNOWN");

        Assert.False(result.IsAllowed);
        Assert.Equal("None", result.ProjectedStepId);
        Assert.Contains("EVT-UNKNOWN", result.Reason ?? string.Empty);
    }

    [Fact]
    public async Task GetReplayTimeline_ReturnsNull_ForUnknownOrCrossTenantInstance()
    {
        var fixture = await SeedLoanApprovalAsync();

        var missing = await fixture.Service.GetReplayTimelineAsync(fixture.TenantId, Guid.NewGuid());
        var otherTenant = await fixture.Service.GetReplayTimelineAsync(Guid.NewGuid(), fixture.Instance.Id);

        Assert.Null(missing);
        Assert.Null(otherTenant);
    }

    [Fact]
    public async Task PlanCompensationPath_UsesRuntimeHistoryAndReturnsReverseCompensationOrder()
    {
        var fixture = await SeedLoanApprovalAsync();

        var result = await fixture.Service.PlanCompensationPathAsync(
            fixture.TenantId,
            fixture.Instance.Id,
            failedStepId: "Pending");

        Assert.NotNull(result);
        Assert.Equal(fixture.Instance.Id, result!.WorkflowInstanceId);
        Assert.Contains("Pending", result.ExecutedStepIds);
        Assert.False(result.IsFullyCompensable);
        Assert.Contains("Start", result.BlockedSteps);
        Assert.Equal("Pending", result.OrderedCompensations.First().StepId);
    }

    private static async Task<TimeTravelFixture> SeedLoanApprovalAsync()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new FlowOSDbContext(options);
        var tenantId = Guid.NewGuid();
        var classId = Guid.NewGuid();

        var definition = new WorkflowDefinition(tenantId, "LoanApproval", 1, "Start");
        var start = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
        start.NextSteps["EVT-SUBMIT"] = "Pending";
        var pending = new WorkflowStepDefinition("Pending", WorkflowStepType.HumanTask);
        pending.NextSteps["EVT-APPROVE"] = "Approved";
        pending.NextSteps["EVT-REJECT"] = "Rejected";
        pending.OnExit.Add(new StepActionDefinition("Webhook") { Target = "https://hooks.example/exit" });
        pending.OnFailure.Add(new StepActionDefinition("Notification") { Target = "ops-oncall" });
        var approved = new WorkflowStepDefinition("Approved", WorkflowStepType.End);
        approved.NextSteps["Default"] = "END";
        var rejected = new WorkflowStepDefinition("Rejected", WorkflowStepType.End);
        rejected.NextSteps["Default"] = "END";
        rejected.OnEntry.Add(new StepActionDefinition("Notification") { Target = "risk-desk" });

        definition.AddStep(start);
        definition.AddStep(pending);
        definition.AddStep(approved);
        definition.AddStep(rejected);

        var instance = new WorkflowInstance(tenantId, definition.Id, classId, 1, "Pending", Guid.NewGuid(), "Pending");

        var started = new StandardEvent(tenantId, "WorkflowStarted");
        started.SetCorrelationId(instance.Id);
        started.AddMetadata("StartStep", "Start");
        started.AddMetadata("InitialState", "Draft");
        SetTimestamp(started, DateTime.UtcNow.AddMinutes(-2));

        var submitted = new StandardEvent(tenantId, "EVT-SUBMIT");
        submitted.SetCorrelationId(instance.Id);
        submitted.AddMetadata("FromStep", "Start");
        submitted.AddMetadata("ToStep", "Pending");
        submitted.AddMetadata("FromState", "Draft");
        submitted.AddMetadata("ToState", "Pending");
        submitted.AddMetadata("Payload", """{"amount":5000}""");
        SetTimestamp(submitted, DateTime.UtcNow.AddMinutes(-1));

        var actionLog = new WorkflowActionExecutionLog(
            tenantId,
            instance.Id,
            "Pending",
            "OnEntry",
            "Webhook",
            "https://hooks.example/pending",
            "Succeeded",
            42,
            200);
        typeof(WorkflowActionExecutionLog)
            .GetField("<ExecutedAtUtc>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(actionLog, submitted.Timestamp);

        db.WorkflowDefinitions.Add(definition);
        db.WorkflowInstances.Add(instance);
        db.Events.AddRange(started, submitted);
        db.ActionExecutionLogs.Add(actionLog);
        await db.SaveChangesAsync();

        var engine = new WorkflowEngine(new StateMachineEngine());
        var planner = new CompensationPlannerService();
        var service = new WorkflowTimeTravelService(db, engine, new StateMachineEngine(), planner);
        return new TimeTravelFixture(db, service, tenantId, instance);
    }

    private static void SetTimestamp(DomainEvent domainEvent, DateTime timestamp)
    {
        typeof(DomainEvent)
            .GetField("<Timestamp>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(domainEvent, timestamp);
    }

    private sealed record TimeTravelFixture(
        FlowOSDbContext Db,
        WorkflowTimeTravelService Service,
        Guid TenantId,
        WorkflowInstance Instance);
}
