using System.Linq;
using System.Threading.Tasks;
using FlowOS.API.Services;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Tools;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

/// <summary>
/// Locks IncidentAlertEscalation: SLA warning alerts, timeout escalation,
/// Critical severity skip, and On-Call inbox capability gate.
/// </summary>
public class IncidentAlertEscalationSimulationTests
{
    private readonly SimulationTools _tools = new(new Mock<IMediator>().Object);

    [Fact]
    public async Task Seeded_StandardSeverity_ResolveFiresAlertsAndReachesEnd()
    {
        var data = await Simulate("Medium", "Support", false, "EVT-OPEN", "EVT-RESOLVE");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Resolved", data["finalState"]?.ToString());
        AssertReminderFired(data, "EVT-SLA-WARN-1H");
        AssertReminderFired(data, "EVT-SLA-WARN-3H");
        Assert.DoesNotContain(Trace(data), action => action.Contains("[SLA Timeout Fired]"));
        AssertNotification(data, "IncidentChannel");
        AssertNotification(data, "SupportInbox");
    }

    [Fact]
    public async Task Seeded_StandardSeverity_OverdueEscalatesToOnCallInbox()
    {
        var data = await Simulate("Medium", "Support", true, "EVT-OPEN");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("OnCallEscalation", data["currentStepId"]?.ToString());
        Assert.Equal("Escalated", data["finalState"]?.ToString());
        AssertReminderFired(data, "EVT-SLA-WARN-1H");
        AssertReminderFired(data, "EVT-SLA-WARN-3H");
        Assert.Contains(Trace(data), action => action.Contains("[SLA Timeout Fired]"));
        AssertRequiredRoles(data, "OnCall");
        AssertNotification(data, "OnCallPager");
    }

    [Fact]
    public async Task Seeded_CriticalSeverity_OpensOnCallInbox()
    {
        var data = await Simulate("Critical", "Reporter", false, "EVT-OPEN");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("OnCallEscalation", data["currentStepId"]?.ToString());
        Assert.Equal("Escalated", data["finalState"]?.ToString());
        AssertRequiredRoles(data, "OnCall");
        Assert.Contains(data["decisionsEvaluated"] as JArray ?? new JArray(), token =>
            token["target"]?.ToString() == "OnCallEscalation" && token["matched"]?.Value<bool>() == true);
    }

    [Fact]
    public async Task Seeded_CriticalSeverity_OnCallCloseReachesEnd()
    {
        var data = await Simulate("Critical", "OnCall", false, "EVT-OPEN", "EVT-CLOSE");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Resolved", data["finalState"]?.ToString());
    }

    [Fact]
    public async Task Seeded_OverdueThenOnCallCloseReachesEnd()
    {
        var data = await Simulate("Medium", "OnCall", true, "EVT-OPEN", "EVT-CLOSE");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Resolved", data["finalState"]?.ToString());
        Assert.Contains(Trace(data), action => action.Contains("[SLA Timeout Fired]"));
    }

    [Fact]
    public async Task Seeded_SupportCannotCloseOnCallInbox()
    {
        var data = await Simulate("Critical", "Support", false, "EVT-OPEN", "EVT-CLOSE");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("OnCallEscalation", data["currentStepId"]?.ToString());
        var pending = data["pendingHumanTask"] as JObject;
        Assert.True(pending?["unauthorizedAttempt"]?.Value<bool>());
        Assert.Equal("EVT-CLOSE", pending?["deniedEvent"]?.ToString());
        AssertRequiredRoles(data, "OnCall");
    }

    [Fact]
    public async Task Seeded_GraphDeclaresAlertsAndEscalation()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"incident-graph-{tenantId}")
            .Options);

        await DataSeeder.SeedFlagshipWorkflowsAsync(db, tenantId);
        var pack = await db.WorkflowClasses.SingleAsync(item => item.Name == "IncidentAlertEscalation");
        Assert.True(new WorkflowClassValidator().Validate(pack).IsValid);

        var l1 = pack.Definition.Workflow.Steps.Single(step => step.StepId == "L1Review");
        Assert.Equal("4h", l1.Sla?.Duration);
        Assert.Equal("EVT-ESCALATE", l1.Sla?.TimeoutEvent);
        Assert.Equal("OnCallEscalation", l1.Sla?.EscalationStepId);
        Assert.Equal("OnCall", l1.Sla?.EscalationRole);
        Assert.Contains(l1.Sla!.Reminders, item => item.TriggerEvent == "EVT-SLA-WARN-1H");
        Assert.Contains(l1.Sla.Reminders, item => item.TriggerEvent == "EVT-SLA-WARN-3H");
        Assert.Equal("OnCallEscalation", pack.Definition.Workflow.Steps
            .Single(step => step.StepId == "CheckSeverity").Conditions["Severity == \"Critical\""]);
        Assert.Contains(pack.Definition.Roles, role => role.Name == "Support");
        Assert.Contains(pack.Definition.Roles, role => role.Name == "OnCall");
    }

    private async Task<JObject> Simulate(string severity, string role, bool autoAdvanceTimers, params string[] events)
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"incident-sim-{tenantId}-{severity}-{role}-{autoAdvanceTimers}")
            .Options);

        await DataSeeder.SeedFlagshipWorkflowsAsync(db, tenantId);
        var pack = await db.WorkflowClasses.SingleAsync(item => item.Name == "IncidentAlertEscalation");

        var call = await _tools.SimulateWorkflowClass(new JObject
        {
            ["blueprint"] = JObject.FromObject(pack.Definition),
            ["name"] = pack.Name,
            ["payload"] = new JObject
            {
                ["TicketId"] = "INC-1042",
                ["Severity"] = severity,
                ["Summary"] = "Checkout latency"
            },
            ["role"] = role,
            ["events"] = new JArray(events),
            ["autoAdvanceTimers"] = autoAdvanceTimers
        });

        Assert.False(call.IsError);
        var json = JObject.Parse(call.Content[0].Text);
        Assert.True(json["ok"]?.Value<bool>(), json["error"]?["message"]?.ToString() ?? json.ToString());
        var data = json["data"] as JObject;
        Assert.NotNull(data);
        return data!;
    }

    private static void AssertReminderFired(JObject data, string eventId)
        => Assert.Contains(Trace(data), action =>
            action.Contains("[SLA Reminder Fired]") && action.Contains(eventId));

    private static void AssertNotification(JObject data, string target)
    {
        var actions = data["actionsTriggered"] as JArray;
        Assert.NotNull(actions);
        Assert.Contains(actions, token =>
            token["actionType"]?.ToString() == "Notification" &&
            token["target"]?.ToString() == target);
    }

    private static void AssertRequiredRoles(JObject data, string expectedRole)
    {
        var pending = data["pendingHumanTask"] as JObject;
        Assert.NotNull(pending);
        var roles = pending["requiredRoles"] as JArray;
        Assert.NotNull(roles);
        Assert.Contains(roles, token => string.Equals(token.ToString(), expectedRole, System.StringComparison.OrdinalIgnoreCase));
    }

    private static string[] Trace(JObject data)
        => (data["executionTrace"] as JArray ?? new JArray())
            .Select(token => token["action"]?.ToString() ?? "")
            .ToArray();
}
