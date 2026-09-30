using FlowOS.Domain.Blueprints;
using FlowOS.MCP.Tools;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;

namespace FlowOS.UnitTests.Workflows;

/// <summary>
/// Wait, alert, then wait again. The simulator must fire each SLA and continue the alert
/// only on the exact nextSteps key Default.
/// </summary>
public class AlertTimerChainSimulationTests
{
    private readonly SimulationTools _tools = new(new Mock<IMediator>().Object);

    [Fact]
    public async Task AutoAdvanceTimers_ExactDefault_FiresBothTimeoutsAndReachesTheEnd()
    {
        var data = await Simulate("Default");

        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Closed", data["finalState"]?.ToString());
        AssertTimeout(data, "T1Done");
        AssertTimeout(data, "T2Done");
        Assert.Contains(Trace(data), action => action.Contains("Alert: Quote not viewed"));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("SystemContinue")]
    public async Task AutoAdvanceTimers_WrongContinueKey_StopsOnTheAlertAndSkipsTheNextTimer(string continueKey)
    {
        var data = await Simulate(continueKey);

        Assert.Equal("Alert1", data["currentStepId"]?.ToString());
        Assert.Equal("Running", data["status"]?.ToString());
        AssertTimeout(data, "T1Done");
        Assert.DoesNotContain(Trace(data), action => action.Contains("T2Done"));
        Assert.Contains(Trace(data), action => action.Contains("exact key 'Default'"));
    }

    private async Task<JObject> Simulate(string alertContinueKey)
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "T1Done" },
                new() { EventId = "T2Done" },
                new() { EventId = "Default" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Waiting1",
                States = new List<string> { "Waiting1", "Alert1Active", "Waiting2", "Closed" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Waiting1", ToState = "Alert1Active", EventId = "T1Done" },
                    new() { FromState = "Alert1Active", ToState = "Waiting2", EventId = "Default" },
                    new() { FromState = "Waiting2", ToState = "Closed", EventId = "T2Done" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Wait1",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "Wait1",
                        StepType = "HumanTask",
                        NextSteps = new Dictionary<string, string> { ["T1Done"] = "Alert1" },
                        Sla = new StepSlaBlueprint { Duration = "1m", TimeoutEvent = "T1Done" }
                    },
                    new()
                    {
                        StepId = "Alert1",
                        StepType = "SystemTask",
                        NextSteps = new Dictionary<string, string> { [alertContinueKey] = "Wait2" },
                        OnEntry = new List<StepActionBlueprint>
                        {
                            new()
                            {
                                ActionType = "Notification",
                                Target = "Sales",
                                Template = "Alert: Quote not viewed"
                            }
                        }
                    },
                    new()
                    {
                        StepId = "Wait2",
                        StepType = "HumanTask",
                        NextSteps = new Dictionary<string, string> { ["T2Done"] = "END" },
                        Sla = new StepSlaBlueprint { Duration = "3m", TimeoutEvent = "T2Done" }
                    }
                }
            }
        };

        var result = await _tools.SimulateWorkflowClass(new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["autoAdvanceTimers"] = true
        });

        Assert.False(result.IsError, result.Content.FirstOrDefault()?.Text);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        return data;
    }

    private static IEnumerable<string> Trace(JObject data) =>
        (data["executionTrace"] as JArray ?? new JArray())
        .Select(item => item["action"]?.ToString() ?? "");

    private static void AssertTimeout(JObject data, string eventId) =>
        Assert.Contains(Trace(data), action =>
            action.Contains("[SLA Timeout Fired]") && action.Contains(eventId));
}
