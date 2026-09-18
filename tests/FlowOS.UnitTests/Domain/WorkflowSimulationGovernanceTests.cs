using System.Linq;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using Xunit;

namespace FlowOS.UnitTests.Domain;

public class WorkflowSimulationGovernanceTests
{
    [Fact]
    public void Apply_StampsHumanTaskCaps_AndGrantsDirectorManagerInboxCaps()
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events =
            {
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit", AllowedRoles = { "User" } },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve", AllowedRoles = { "Manager" } },
                new EventBlueprint { EventId = "EVT-REJECT", Name = "Reject", AllowedRoles = { "Manager" } }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Draft",
                Steps =
                {
                    new StepBlueprint
                    {
                        StepId = "Draft",
                        StepType = "HumanTask",
                        RequiredRoles = { "User" },
                        NextSteps = { { "EVT-SUBMIT", "Pending" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "Pending",
                        StepType = "HumanTask",
                        RequiredRoles = { "Manager" },
                        NextSteps = { { "EVT-APPROVE", "END" }, { "EVT-REJECT", "END" } }
                    }
                }
            }
        };

        WorkflowSimulationGovernance.Apply(blueprint);

        var pending = blueprint.Workflow.Steps.Single(step => step.StepId == "Pending");
        Assert.Contains("event.publish.EVT-APPROVE", pending.RequiredCapabilities);
        Assert.Contains("event.publish.EVT-REJECT", pending.RequiredCapabilities);

        var approve = blueprint.Events.Single(evt => evt.EventId == "EVT-APPROVE");
        Assert.Equal(EventCategory.Human, approve.Category);
        Assert.Contains("event.publish.EVT-APPROVE", approve.RequiredCapabilities);

        var manager = blueprint.Roles.Single(role => role.Name == "Manager");
        Assert.Contains("event.publish.EVT-APPROVE", manager.GrantedCapabilities);

        var director = blueprint.Roles.Single(role => role.Name == "Director");
        Assert.Contains("event.publish.EVT-APPROVE", director.GrantedCapabilities);
        Assert.Contains("event.publish.EVT-REJECT", director.GrantedCapabilities);

        Assert.Contains(blueprint.Capabilities, cap => cap.Code == "event.publish.EVT-APPROVE");
    }
}
