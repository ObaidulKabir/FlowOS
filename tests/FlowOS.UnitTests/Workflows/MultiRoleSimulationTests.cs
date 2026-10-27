using System.Threading.Tasks;
using FlowOS.Domain.Blueprints;
using FlowOS.MCP.Tools;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class MultiRoleSimulationTests
{
    private readonly SimulationTools _tools = new(new Mock<IMediator>().Object);

    [Fact]
    public async Task Single_role_stops_when_the_next_human_task_belongs_to_someone_else()
    {
        var data = await Simulate("Applicant");

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("Review", data["currentStepId"]?.ToString());
        Assert.Equal(true, data["pendingHumanTask"]?["unauthorizedAttempt"]?.Value<bool>());
        Assert.Equal("EVT-REVIEW", data["pendingHumanTask"]?["deniedEvent"]?.ToString());
        Assert.Equal("Applicant", data["pendingHumanTask"]?["simulatedRole"]?.ToString());
    }

    [Fact]
    public async Task Role_map_continues_each_human_task_as_its_mapped_role()
    {
        var data = await Simulate("Applicant", new JObject
        {
            ["Submit"] = "Applicant",
            ["Review"] = "Planner"
        });

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Approved", data["finalState"]?.ToString());
        Assert.Equal("Applicant", data["roleMap"]?["Submit"]?.ToString());
        Assert.Equal("Planner", data["roleMap"]?["Review"]?.ToString());
        Assert.Contains("role 'Planner'", data["executionTrace"]?.ToString());
    }

    [Fact]
    public async Task Role_map_still_denies_a_mapped_role_that_lacks_the_capability()
    {
        var data = await Simulate("Applicant", new JObject
        {
            ["Submit"] = "Applicant",
            ["Review"] = "Applicant"
        });

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("Review", data["currentStepId"]?.ToString());
        Assert.Equal(true, data["pendingHumanTask"]?["unauthorizedAttempt"]?.Value<bool>());
        Assert.Equal("Applicant", data["pendingHumanTask"]?["simulatedRole"]?.ToString());
    }

    [Fact]
    public async Task Event_key_in_the_role_map_selects_the_role_for_that_event()
    {
        var data = await Simulate("Applicant", new JObject
        {
            ["EVT-REVIEW"] = "Planner"
        });

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("Planner", data["roleMap"]?["Review"]?.ToString());
    }

    private async Task<JObject> Simulate(string role, JObject? roleMap = null)
    {
        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(PermitBlueprint()),
            ["role"] = role,
            ["events"] = new JArray("EVT-SUBMIT", "EVT-REVIEW")
        };
        if (roleMap != null)
            args["roleMap"] = roleMap;

        var call = await _tools.SimulateWorkflowClass(args);
        Assert.False(call.IsError, call.Content[0].Text);
        var data = JObject.Parse(call.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        return data!;
    }

    private static WorkflowClassBlueprint PermitBlueprint() => new()
    {
        Events =
        [
            new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit", AllowedRoles = ["Applicant"] },
            new EventBlueprint { EventId = "EVT-REVIEW", Name = "Review", AllowedRoles = ["Planner"] }
        ],
        Roles =
        [
            new RoleBlueprint { Name = "Applicant", GrantedCapabilities = ["event.publish.EVT-SUBMIT"] },
            new RoleBlueprint { Name = "Planner", GrantedCapabilities = ["event.publish.EVT-REVIEW"] }
        ],
        StateMachine = new StateMachineBlueprint
        {
            InitialState = "Draft",
            States = ["Draft", "InReview", "Approved"],
            Transitions =
            [
                new TransitionBlueprint { FromState = "Draft", ToState = "InReview", EventId = "EVT-SUBMIT" },
                new TransitionBlueprint { FromState = "InReview", ToState = "Approved", EventId = "EVT-REVIEW" }
            ]
        },
        Workflow = new WorkflowBlueprint
        {
            StartStepId = "Submit",
            Steps =
            [
                new StepBlueprint
                {
                    StepId = "Submit",
                    StepType = "HumanTask",
                    RequiredRoles = ["Applicant"],
                    NextSteps = new() { ["EVT-SUBMIT"] = "Review" }
                },
                new StepBlueprint
                {
                    StepId = "Review",
                    StepType = "HumanTask",
                    RequiredRoles = ["Planner"],
                    NextSteps = new() { ["EVT-REVIEW"] = "END" }
                }
            ]
        }
    };
}
