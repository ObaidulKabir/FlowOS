using System.Linq;
using System.Threading.Tasks;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Services;
using FlowOS.MCP.Tools;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

/// <summary>
/// ExpenseApprovalV3 moves the Manager/Director escalation threshold out of the ExpenseApp Node
/// client (which used to compute `amount > 100` in JavaScript before choosing whether to publish
/// EVT-APPROVE or EVT-ESCALATE) and into the blueprint itself: PendingManager always routes a
/// single EVT-APPROVE through a CheckApprovalAmount Decision step, paired with two Law
/// transitions guarding the same EventId from the same FromState - exactly the pattern
/// ExpenseApprovalV2's CheckAmount already proves at submission time (see
/// ExpenseApprovalAmountRoleTests), just moved to the approval decision.
///
/// These tests reproduce DataSeeder.CreateExpenseApprovalV3Blueprint's shape locally (DataSeeder
/// lives in FlowOS.Api and the factory is private, matching the existing convention in
/// ExpenseApprovalAmountRoleTests.CreateAmountRoutedBlueprint for V2) so the client-facing
/// contract - "the client always publishes EVT-APPROVE; FlowOS alone decides where it lands" -
/// is pinned independently of that assembly.
/// </summary>
public class ExpenseApprovalV3ClientDecisionTests
{
    private readonly SimulationTools _tools = new(new Mock<IMediator>().Object);
    private const string EscalationCondition = "Amount > 100";

    [Fact]
    public async Task LowAmount_SingleApproveEvent_FinalizesWithoutEscalation()
    {
        // DX report F1 fix: the Draft step's Command/non-agent-actor wait now also enforces
        // EVT-SUBMIT's declared AllowedRoles ("Employee"/"User"), so submitting it needs a roleMap
        // override distinct from the "Manager" role used for the rest of this sequence.
        var data = await Simulate(
            50, "Manager", new JObject { ["EVT-SUBMIT"] = "User" }, "EVT-SUBMIT", "EVT-APPROVE");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Approved", data["finalState"]?.ToString());
    }

    [Fact]
    public async Task HighAmount_SameSingleApproveEvent_EscalatesToDirectorInstead()
    {
        // The client publishes the exact same EVT-APPROVE as the low-amount case above - no
        // amount-aware branching on the client side - yet FlowOS lands the instance on
        // PendingDirector, not Approved, because the blueprint's own Decision step and guarded
        // Law transition read Amount from the payload.
        var data = await Simulate(
            500, "Manager", new JObject { ["EVT-SUBMIT"] = "User" }, "EVT-SUBMIT", "EVT-APPROVE");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("PendingDirector", data["currentStepId"]?.ToString());
        Assert.Equal("PendingDirector", data["finalState"]?.ToString());
    }

    [Fact]
    public async Task HighAmount_AfterEscalation_DirectorApproveReachesEnd()
    {
        // EVT-APPROVE is still the Manager's event (its step keeps RequiredRoles: ["Manager"]) even
        // though the blueprint's own Decision step routes a high-amount approval on to
        // PendingDirector; only EVT-DIRECTOR-APPROVE is the Director's. A single `role` across the
        // whole sequence would authorize only one of the two actors, so this test pins each event to
        // its real actor with an explicit roleMap (keyed by event id, per FlowOsMcpGuidance's
        // "A dry run with several human roles passes roleMap on simulate_workflowclass") rather than
        // leaning on single-role leniency.
        var data = await Simulate(
            500,
            "Manager",
            new JObject { ["EVT-SUBMIT"] = "User", ["EVT-APPROVE"] = "Manager", ["EVT-DIRECTOR-APPROVE"] = "Director" },
            "EVT-SUBMIT", "EVT-APPROVE", "EVT-DIRECTOR-APPROVE");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Approved", data["finalState"]?.ToString());
    }

    [Fact]
    public async Task ManualEscalate_StillAvailable_BelowThreshold()
    {
        // A Manager may still choose to escalate a low-value request on their own judgment -
        // EVT-ESCALATE is not removed by moving the automatic threshold into the blueprint.
        var data = await Simulate(
            50, "Manager", new JObject { ["EVT-SUBMIT"] = "User" }, "EVT-SUBMIT", "EVT-ESCALATE");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("PendingDirector", data["currentStepId"]?.ToString());
    }

    [Fact]
    public async Task ManagerReject_EndsAtRejected()
    {
        var data = await Simulate(
            50, "Manager", new JObject { ["EVT-SUBMIT"] = "User" }, "EVT-SUBMIT", "EVT-REJECT");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Rejected", data["finalState"]?.ToString());
    }

    [Fact]
    public void Graph_DeclaresDecisionStepAndGuardedLawOnApprove()
    {
        var blueprint = CreateV3Blueprint();

        var check = blueprint.Workflow.Steps.Single(step => step.StepId == "CheckApprovalAmount");
        Assert.Equal("Decision", check.StepType);
        Assert.Equal("PendingDirector", check.Conditions[EscalationCondition]);
        Assert.Equal("Approved", check.Conditions["Default"]);

        var pendingManager = blueprint.Workflow.Steps.Single(step => step.StepId == "PendingManager");
        Assert.Equal("CheckApprovalAmount", pendingManager.NextSteps["EVT-APPROVE"]);

        var approveTransitions = blueprint.StateMachine.Transitions
            .Where(item => item.FromState == "PendingManager" && item.EventId == "EVT-APPROVE")
            .ToList();
        Assert.Contains(approveTransitions, item => item.ToState == "PendingDirector" && item.Condition == EscalationCondition);
        Assert.Contains(approveTransitions, item => item.ToState == "Approved" && string.IsNullOrWhiteSpace(item.Condition));

        var validation = new WorkflowClassValidator().Validate(blueprint);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    private async Task<JObject> Simulate(int amount, string role, params string[] events) =>
        await Simulate(amount, role, roleMap: null, events);

    private async Task<JObject> Simulate(int amount, string role, JObject? roleMap, params string[] events)
    {
        var blueprint = CreateV3Blueprint();

        var request = new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["name"] = "ExpenseApprovalV3",
            ["payload"] = new JObject { ["Amount"] = amount, ["Currency"] = "USD" },
            ["role"] = role,
            ["events"] = new JArray(events)
        };
        if (roleMap != null)
            request["roleMap"] = roleMap;

        var call = await _tools.SimulateWorkflowClass(request);

        Assert.False(call.IsError);
        var json = JObject.Parse(call.Content[0].Text);
        Assert.True(json["ok"]?.Value<bool>(), json["error"]?["message"]?.ToString() ?? json.ToString());
        var data = json["data"] as JObject;
        Assert.NotNull(data);
        return data!;
    }

    private static WorkflowClassBlueprint CreateV3Blueprint()
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new()
            {
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request", AllowedRoles = new() { "Employee", "User" } },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request", AllowedRoles = new() { "Manager" } },
                new EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request", AllowedRoles = new() { "Manager" } },
                new EventBlueprint { EventId = "EVT-DIRECTOR-APPROVE", Name = "Director Approve", AllowedRoles = new() { "Director" } },
                new EventBlueprint { EventId = "EVT-DIRECTOR-REJECT", Name = "Director Reject", AllowedRoles = new() { "Director" } },
                new EventBlueprint { EventId = "EVT-ESCALATE", Name = "Escalate to Director", AllowedRoles = new() { "Manager" } }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = new() { "Draft", "PendingManager", "PendingDirector", "Approved", "Rejected" },
                Transitions = new()
                {
                    new TransitionBlueprint { FromState = "Draft", ToState = "PendingManager", EventId = "EVT-SUBMIT" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "PendingDirector", EventId = "EVT-APPROVE", Condition = EscalationCondition },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Approved", EventId = "EVT-APPROVE" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "PendingDirector", EventId = "EVT-ESCALATE" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Rejected", EventId = "EVT-REJECT" },
                    new TransitionBlueprint { FromState = "PendingDirector", ToState = "Approved", EventId = "EVT-DIRECTOR-APPROVE" },
                    new TransitionBlueprint { FromState = "PendingDirector", ToState = "Rejected", EventId = "EVT-DIRECTOR-REJECT" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Draft",
                Steps = new()
                {
                    new StepBlueprint { StepId = "Draft", StepType = "Command", NextSteps = new() { { "EVT-SUBMIT", "PendingManager" } } },
                    new StepBlueprint
                    {
                        StepId = "PendingManager",
                        StepType = "HumanTask",
                        RequiredRoles = new() { "Manager" },
                        NextSteps = new()
                        {
                            { "EVT-APPROVE", "CheckApprovalAmount" },
                            { "EVT-ESCALATE", "PendingDirector" },
                            { "EVT-REJECT", "Rejected" }
                        }
                    },
                    new StepBlueprint
                    {
                        StepId = "CheckApprovalAmount",
                        StepType = "Decision",
                        RequiredRoles = new() { "System" },
                        Conditions = new()
                        {
                            { EscalationCondition, "PendingDirector" },
                            { "Default", "Approved" }
                        },
                        NextSteps = new() { { "Default", "Approved" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "PendingDirector",
                        StepType = "HumanTask",
                        RequiredRoles = new() { "Director" },
                        NextSteps = new()
                        {
                            { "EVT-DIRECTOR-APPROVE", "Approved" },
                            { "EVT-DIRECTOR-REJECT", "Rejected" }
                        }
                    },
                    new StepBlueprint { StepId = "Approved", StepType = "Command", NextSteps = new() { { "Default", "END" } } },
                    new StepBlueprint { StepId = "Rejected", StepType = "Command", NextSteps = new() { { "Default", "END" } } }
                }
            }
        };

        WorkflowSimulationGovernance.Apply(blueprint);
        return blueprint;
    }
}
