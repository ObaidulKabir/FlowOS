using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.API.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
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
/// Locks ExpenseApprovalV2 payload routing: Amount &gt; 5000 requires Director,
/// otherwise Manager. Covers Work Decision, Law guards, and inbox roles.
/// </summary>
public class ExpenseApprovalAmountRoleTests
{
    private readonly SimulationTools _tools = new(new Mock<IMediator>().Object);

    [Fact]
    public async Task SeededV2_HighAmount_SubmitLandsOnDirectorInbox()
    {
        var data = await SimulateSeededV2(7500, "User", "EVT-SUBMIT");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("PendingDirector", data["currentStepId"]?.ToString());
        Assert.Equal("PendingDirector", data["finalState"]?.ToString());
        AssertRequiredRoles(data, "Director");
        AssertDecisionTarget(data, "PendingDirector");
    }

    [Fact]
    public async Task SeededV2_LowAmount_SubmitLandsOnManagerInbox()
    {
        var data = await SimulateSeededV2(450, "User", "EVT-SUBMIT");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("PendingManager", data["currentStepId"]?.ToString());
        Assert.Equal("PendingManager", data["finalState"]?.ToString());
        AssertRequiredRoles(data, "Manager");
        AssertDecisionTarget(data, "PendingManager");
    }

    [Fact]
    public async Task SeededV2_HighAmount_DirectorApproveReachesEnd()
    {
        var data = await SimulateSeededV2(7500, "Director", "EVT-SUBMIT", "EVT-DIRECTOR-APPROVE");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Approved", data["finalState"]?.ToString());
    }

    [Fact]
    public async Task SeededV2_LowAmount_ManagerApproveReachesEnd()
    {
        var data = await SimulateSeededV2(450, "Manager", "EVT-SUBMIT", "EVT-APPROVE");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Approved", data["finalState"]?.ToString());
    }

    [Fact]
    public async Task SeededV2_HighAmount_ManagerCannotSignDirectorInbox()
    {
        var data = await SimulateSeededV2(7500, "Manager", "EVT-SUBMIT", "EVT-DIRECTOR-APPROVE");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("PendingDirector", data["currentStepId"]?.ToString());
        Assert.Equal("PendingDirector", data["finalState"]?.ToString());
        var pending = data["pendingHumanTask"] as JObject;
        Assert.True(pending?["unauthorizedAttempt"]?.Value<bool>());
        AssertRequiredRoles(data, "Director");
    }

    [Fact]
    public async Task InlineBlueprint_DualSubmitGuards_DoNotBlockLowAmount()
    {
        var blueprint = CreateAmountRoutedBlueprint();
        var call = await _tools.SimulateWorkflowClass(new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["payload"] = new JObject { ["Amount"] = 450 },
            ["role"] = "User",
            ["events"] = new JArray("EVT-SUBMIT")
        });

        Assert.False(call.IsError);
        var data = JObject.Parse(call.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.NotEqual("BlockedByGuard", data["status"]?.ToString());
        Assert.Equal("PendingManager", data["currentStepId"]?.ToString());
        Assert.Equal("PendingManager", data["finalState"]?.ToString());
        AssertRequiredRoles(data!, "Manager");
    }

    [Fact]
    public async Task InlineBlueprint_BothSubmitGuardsFail_BlocksAtDraft()
    {
        var blueprint = CreateAmountRoutedBlueprint();
        var transitions = blueprint.StateMachine.Transitions;
        transitions.RemoveAll(item => item.FromState == "Draft" && item.EventId == "EVT-SUBMIT");
        transitions.Insert(0, new TransitionBlueprint
        {
            FromState = "Draft",
            ToState = "PendingDirector",
            EventId = "EVT-SUBMIT",
            Condition = "Amount > 999999"
        });
        transitions.Insert(1, new TransitionBlueprint
        {
            FromState = "Draft",
            ToState = "PendingManager",
            EventId = "EVT-SUBMIT",
            Condition = "Amount > 999999"
        });

        var call = await _tools.SimulateWorkflowClass(new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["payload"] = new JObject { ["Amount"] = 100 },
            ["role"] = "User",
            ["events"] = new JArray("EVT-SUBMIT")
        });

        Assert.False(call.IsError);
        var data = JObject.Parse(call.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("BlockedByGuard", data["status"]?.ToString());
        Assert.Equal("Draft", data["currentStepId"]?.ToString());
        Assert.Equal("Draft", data["finalState"]?.ToString());
    }

    [Fact]
    public async Task SeededV2_GraphDeclaresAmountDecisionAndGuardedLaw()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"amount-role-graph-{tenantId}")
            .Options);

        await EnsureExpenseApprovalV2Async(db, tenantId);

        var v2 = await db.WorkflowClasses.SingleAsync(item => item.Name == "ExpenseApprovalV2");
        var check = v2.Definition.Workflow.Steps.Single(step => step.StepId == "CheckAmount");
        Assert.Equal("Decision", check.StepType);
        Assert.Equal("PendingDirector", check.Conditions["Amount > 5000"]);
        Assert.Equal("PendingManager", check.Conditions["Default"]);
        Assert.Equal("CheckAmount", v2.Definition.Workflow.Steps.Single(step => step.StepId == "Draft").NextSteps["EVT-SUBMIT"]);

        var submitTransitions = v2.Definition.StateMachine.Transitions
            .Where(item => item.FromState == "Draft" && item.EventId == "EVT-SUBMIT")
            .ToList();
        Assert.Contains(submitTransitions, item =>
            item.ToState == "PendingDirector" && item.Condition == "Amount > 5000");
        Assert.Contains(submitTransitions, item =>
            item.ToState == "PendingManager" && string.IsNullOrWhiteSpace(item.Condition));
        Assert.Contains(v2.Definition.Workflow.Steps.Single(step => step.StepId == "PendingDirector").RequiredRoles, role => role == "Director");
        Assert.Contains(v2.Definition.Workflow.Steps.Single(step => step.StepId == "PendingManager").RequiredRoles, role => role == "Manager");
    }

    private async Task<JObject> SimulateSeededV2(int amount, string role, params string[] events)
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"amount-role-sim-{tenantId}-{amount}-{role}")
            .Options);

        await EnsureExpenseApprovalV2Async(db, tenantId);
        var v2 = await db.WorkflowClasses.SingleAsync(item => item.Name == "ExpenseApprovalV2");

        var call = await _tools.SimulateWorkflowClass(new JObject
        {
            ["blueprint"] = JObject.FromObject(v2.Definition),
            ["name"] = v2.Name,
            ["payload"] = new JObject { ["Amount"] = amount, ["Currency"] = "USD" },
            ["role"] = role,
            ["events"] = new JArray(events)
        });

        Assert.False(call.IsError);
        var json = JObject.Parse(call.Content[0].Text);
        Assert.True(json["ok"]?.Value<bool>(), json["error"]?["message"]?.ToString() ?? json.ToString());
        var data = json["data"] as JObject;
        Assert.NotNull(data);
        return data!;
    }

    private static void AssertRequiredRoles(JObject data, string expectedRole)
    {
        var pending = data["pendingHumanTask"] as JObject;
        Assert.NotNull(pending);
        var roles = pending["requiredRoles"] as JArray;
        Assert.NotNull(roles);
        Assert.Contains(roles, token => string.Equals(token.ToString(), expectedRole, System.StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertDecisionTarget(JObject data, string expectedTarget)
    {
        var decisions = data["decisionsEvaluated"] as JArray;
        Assert.NotNull(decisions);
        Assert.Contains(decisions, token =>
            string.Equals(token["target"]?.ToString(), expectedTarget, System.StringComparison.OrdinalIgnoreCase) &&
            token["matched"]?.Value<bool>() == true);
    }

    private static async Task EnsureExpenseApprovalV2Async(FlowOSDbContext db, Guid tenantId)
    {
        if (!await db.WorkflowClasses.AnyAsync(item => item.TenantId == tenantId && item.Name == "ExpenseApprovalV2"))
        {
            var stub = CreateAmountRoutedBlueprint();
            var workflowClass = new WorkflowClass(tenantId, "ExpenseApprovalV2", "1.0.0", stub);
            Assert.True(new WorkflowClassManager().Publish(workflowClass).IsValid);
            db.WorkflowClasses.Add(workflowClass);
            await db.SaveChangesAsync();
        }

        await DataSeeder.RepairExpenseApprovalV2GraphAsync(db, tenantId);
    }

    private static WorkflowClassBlueprint CreateAmountRoutedBlueprint()
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events =
            [
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit", AllowedRoles = ["User"] },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve", AllowedRoles = ["Manager"] },
                new EventBlueprint { EventId = "EVT-DIRECTOR-APPROVE", Name = "Director Approve", AllowedRoles = ["Director"] }
            ],
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = ["Draft", "PendingManager", "PendingDirector", "Approved"],
                Transitions =
                [
                    new TransitionBlueprint { FromState = "Draft", ToState = "PendingDirector", EventId = "EVT-SUBMIT", Condition = "Amount > 5000" },
                    new TransitionBlueprint { FromState = "Draft", ToState = "PendingManager", EventId = "EVT-SUBMIT" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Approved", EventId = "EVT-APPROVE" },
                    new TransitionBlueprint { FromState = "PendingDirector", ToState = "Approved", EventId = "EVT-DIRECTOR-APPROVE" }
                ]
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Draft",
                Steps =
                [
                    new StepBlueprint { StepId = "Draft", StepType = "Command", NextSteps = new() { ["EVT-SUBMIT"] = "CheckAmount" } },
                    new StepBlueprint
                    {
                        StepId = "CheckAmount",
                        StepType = "Decision",
                        Conditions = new() { ["Amount > 5000"] = "PendingDirector", ["Default"] = "PendingManager" },
                        NextSteps = new() { ["Default"] = "PendingManager" }
                    },
                    new StepBlueprint
                    {
                        StepId = "PendingManager",
                        StepType = "HumanTask",
                        RequiredRoles = ["Manager"],
                        NextSteps = new() { ["EVT-APPROVE"] = "Approved" }
                    },
                    new StepBlueprint
                    {
                        StepId = "PendingDirector",
                        StepType = "HumanTask",
                        RequiredRoles = ["Director"],
                        NextSteps = new() { ["EVT-DIRECTOR-APPROVE"] = "Approved" }
                    },
                    new StepBlueprint { StepId = "Approved", StepType = "Command", NextSteps = new() { ["Default"] = "END" } }
                ]
            }
        };
        WorkflowSimulationGovernance.Apply(blueprint);
        return blueprint;
    }
}
