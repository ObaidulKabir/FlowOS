using System.Linq;
using System.Threading.Tasks;
using FlowOS.API.Services;
using FlowOS.Domain.Enums;
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
/// Locks QuoteAutoReview: Agent actor auto-commits in-bound quotes,
/// over-limit Advisor inbox, park vs overdue, and human override.
/// </summary>
public class QuoteAutoReviewSimulationTests
{
    private readonly SimulationTools _tools = new(new Mock<IMediator>().Object);

    [Fact]
    public async Task Seeded_InBound_SubmitOnly_AgentAutoAcceptsToEnd()
    {
        var data = await Simulate(900, "Submitter", timers: false, agents: true, null, "EVT-SUBMIT");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Accepted", data["finalState"]?.ToString());
        Assert.Contains(Trace(data), action => action.Contains("[Agent Auto-Commit]") && action.Contains("EVT-ACCEPT"));
        Assert.DoesNotContain(Trace(data), action => action.Contains("[SLA Timeout Fired]"));
        AssertNotification(data, "QuoteDesk");
        Assert.Contains(data["decisionsEvaluated"] as JArray ?? new JArray(), token =>
            token["target"]?.ToString() == "AgentReview" && token["matched"]?.Value<bool>() == true);
    }

    [Fact]
    public async Task Seeded_OverLimit_SubmitOpensAdvisorInbox()
    {
        var data = await Simulate(5000, "Submitter", timers: false, agents: true, null, "EVT-SUBMIT");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("AdvisorReview", data["currentStepId"]?.ToString());
        Assert.Equal("AdvisorQueued", data["finalState"]?.ToString());
        Assert.DoesNotContain(Trace(data), action => action.Contains("[Agent Auto-Commit]"));
        AssertRequiredRoles(data, "Advisor");
        Assert.Equal("Human", data["pendingHumanTask"]?["actor"]?.ToString());
        AssertNotification(data, "AdvisorInbox");
    }

    [Fact]
    public async Task Seeded_InBound_LowConfidence_ParksAtAgentReview()
    {
        var data = await Simulate(
            900,
            "Submitter",
            timers: false,
            agents: true,
            new JObject { ["event"] = "EVT-ACCEPT", ["confidence"] = 0.4 },
            "EVT-SUBMIT");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("AgentReview", data["currentStepId"]?.ToString());
        Assert.Equal("AgentQueued", data["finalState"]?.ToString());
        Assert.Contains(Trace(data), action => action.Contains("[Agent Parked]") && action.Contains("minConfidence"));
        var pending = data["pendingAgentTask"] as JObject;
        Assert.NotNull(pending);
        Assert.Equal("EVT-ACCEPT", pending["suggestedEvent"]?.ToString());
        Assert.True(data["pendingHumanTask"]?["parkedByAgent"]?.Value<bool>());
        AssertRequiredRoles(data, "QuoteAgent");
    }

    [Fact]
    public async Task Seeded_InBound_RevisionSuggestion_ParksBecauseNotAutoCommit()
    {
        var data = await Simulate(
            900,
            "Submitter",
            timers: false,
            agents: true,
            new JObject { ["event"] = "EVT-REQUEST-REVISION", ["confidence"] = 0.99 },
            "EVT-SUBMIT");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("AgentReview", data["currentStepId"]?.ToString());
        Assert.Contains(Trace(data), action =>
            action.Contains("[Agent Parked]") && action.Contains("autoCommit.allowedEvents"));
    }

    [Fact]
    public async Task Seeded_InBound_AdvisorOverrideRequestsRevision()
    {
        var data = await Simulate(900, "Advisor", timers: false, agents: true, null, "EVT-SUBMIT", "EVT-REQUEST-REVISION");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("RevisionRequested", data["finalState"]?.ToString());
        Assert.DoesNotContain(Trace(data), action => action.Contains("[Agent Auto-Commit]"));
        Assert.Contains(Trace(data), action => action.Contains("EVT-REQUEST-REVISION"));
    }

    [Fact]
    public async Task Seeded_InBound_AgentDisabled_OverdueFiresTimeout()
    {
        var data = await Simulate(900, "Submitter", timers: true, agents: false, null, "EVT-SUBMIT");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Overdue", data["finalState"]?.ToString());
        Assert.Contains(Trace(data), action => action.Contains("[SLA Reminder Fired]") && action.Contains("EVT-SLA-WARN-4H"));
        Assert.Contains(Trace(data), action => action.Contains("[SLA Timeout Fired]") && action.Contains("EVT-QUOTE-OVERDUE"));
        Assert.DoesNotContain(Trace(data), action => action.Contains("[Agent Auto-Commit]"));
    }

    [Fact]
    public async Task Seeded_GraphDeclaresAgentActorAndAutoCommit()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"quote-graph-{tenantId}")
            .Options);

        await DataSeeder.SeedFlagshipWorkflowsAsync(db, tenantId);
        var pack = await db.WorkflowClasses.SingleAsync(item => item.Name == "QuoteAutoReview");
        Assert.True(new WorkflowClassValidator().Validate(pack).IsValid);

        var agent = pack.Definition.Workflow.Steps.Single(step => step.StepId == "AgentReview");
        Assert.Equal(StepActor.Agent, agent.Actor);
        Assert.Equal(0.9, agent.AutoCommit?.MinConfidence);
        Assert.Contains("EVT-ACCEPT", agent.AutoCommit!.AllowedEvents);
        Assert.DoesNotContain("EVT-QUOTE-OVERDUE", agent.AutoCommit.AllowedEvents);
        Assert.False(string.IsNullOrWhiteSpace(agent.DecisionGuideline));
        Assert.Equal("quote-approval", agent.AgentPrompt);
        Assert.Equal("quote-llm", agent.AgentProvider);
        Assert.Equal("24h", agent.Sla?.Duration);
        Assert.Equal("EVT-QUOTE-OVERDUE", agent.Sla?.TimeoutEvent);
        Assert.Equal("AdvisorReview", pack.Definition.Workflow.Steps
            .Single(step => step.StepId == "CheckAmount").Conditions["Amount > 1500"]);
        Assert.Contains(pack.Definition.Roles, role => role.Name == "QuoteAgent");
        Assert.Contains(pack.Definition.Roles, role => role.Name == "Advisor");
    }

    private async Task<JObject> Simulate(
        long amount,
        string role,
        bool timers,
        bool agents,
        JObject? simulatedAgent,
        params string[] events)
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"quote-sim-{tenantId}-{amount}-{role}-{timers}-{agents}")
            .Options);

        await DataSeeder.SeedFlagshipWorkflowsAsync(db, tenantId);
        var pack = await db.WorkflowClasses.SingleAsync(item => item.Name == "QuoteAutoReview");

        var callArgs = new JObject
        {
            ["blueprint"] = JObject.FromObject(pack.Definition),
            ["name"] = pack.Name,
            ["payload"] = new JObject
            {
                ["QuoteId"] = "Q-2048",
                ["Amount"] = amount,
                ["Estimate"] = amount
            },
            ["role"] = role,
            ["events"] = new JArray(events),
            ["autoAdvanceTimers"] = timers,
            ["autoAdvanceAgents"] = agents
        };
        if (simulatedAgent != null)
            callArgs["simulatedAgent"] = simulatedAgent;

        var call = await _tools.SimulateWorkflowClass(callArgs);
        Assert.False(call.IsError);
        var json = JObject.Parse(call.Content[0].Text);
        Assert.True(json["ok"]?.Value<bool>(), json["error"]?["message"]?.ToString() ?? json.ToString());
        var data = json["data"] as JObject;
        Assert.NotNull(data);
        return data!;
    }

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
        Assert.Contains(roles, token =>
            string.Equals(token.ToString(), expectedRole, System.StringComparison.OrdinalIgnoreCase));
    }

    private static string[] Trace(JObject data)
        => (data["executionTrace"] as JArray ?? new JArray())
            .Select(token => token["action"]?.ToString() ?? "")
            .ToArray();
}
