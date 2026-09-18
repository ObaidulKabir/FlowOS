using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;

namespace FlowOS.UnitTests.Domain;

public class WorkflowClassValidator_CapabilityTests
{
    private readonly WorkflowClassValidator _validator = new();

    [Fact]
    public void Validate_GovernedHumanTaskWithoutCapabilities_ReturnsGov002()
    {
        var blueprint = CreateGovernedBlueprint();
        blueprint.Workflow.Steps[1] = blueprint.Workflow.Steps[1] with
        {
            RequiredCapabilities = new List<string>()
        };
        blueprint.Events[1] = blueprint.Events[1] with
        {
            RequiredCapabilities = new List<string>()
        };

        var result = _validator.Validate(new WorkflowClass(Guid.NewGuid(), "Expense", "1.0.0", blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "GOV-002");
    }

    [Fact]
    public void Validate_RequiredCapabilityMissingFromCatalog_ReturnsGov003()
    {
        var blueprint = CreateGovernedBlueprint();
        blueprint.Workflow.Steps[1] = blueprint.Workflow.Steps[1] with
        {
            RequiredCapabilities = new List<string> { "expense.approve" }
        };

        var result = _validator.Validate(new WorkflowClass(Guid.NewGuid(), "Expense", "1.0.0", blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "GOV-003" && error.Message.Contains("expense.approve"));
    }

    [Fact]
    public void Validate_UngovernedHumanTask_DoesNotRequireCapabilities()
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "EVT-APPROVE", Name = "Approve" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Pending",
                States = new List<string> { "Pending", "Approved" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Review",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "Review",
                        StepType = "HumanTask",
                        NextSteps = new Dictionary<string, string> { ["EVT-APPROVE"] = "END" }
                    }
                }
            }
        };

        var result = _validator.Validate(new WorkflowClass(Guid.NewGuid(), "Legacy", "1.0.0", blueprint));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Message}")));
    }

    private static WorkflowClassBlueprint CreateGovernedBlueprint()
        => new()
        {
            Events = new List<EventBlueprint>
            {
                new()
                {
                    EventId = "EVT-SUBMIT",
                    Name = "Submit",
                    Category = EventCategory.System
                },
                new()
                {
                    EventId = "EVT-APPROVE",
                    Name = "Approve",
                    Category = EventCategory.Human,
                    RequiredCapabilities = new List<string> { "event.publish.EVT-APPROVE" }
                }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = new List<string> { "Draft", "Pending", "Approved" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Draft", ToState = "Pending", EventId = "EVT-SUBMIT" },
                    new() { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Submit",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "Submit",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { ["EVT-SUBMIT"] = "Review" }
                    },
                    new()
                    {
                        StepId = "Review",
                        StepType = "HumanTask",
                        RequiredRoles = new List<string> { "Approver" },
                        RequiredCapabilities = new List<string> { "event.publish.EVT-APPROVE" },
                        NextSteps = new Dictionary<string, string> { ["EVT-APPROVE"] = "END" }
                    }
                }
            },
            Roles = new List<RoleBlueprint>
            {
                new()
                {
                    Name = "Approver",
                    GrantedCapabilities = new List<string> { "event.publish.EVT-APPROVE" }
                }
            },
            Capabilities = new List<CapabilityBlueprint>
            {
                new() { Code = "event.publish.EVT-APPROVE" }
            }
        };
}
