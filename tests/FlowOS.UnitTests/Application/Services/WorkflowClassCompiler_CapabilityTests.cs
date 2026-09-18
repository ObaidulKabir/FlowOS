using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects;
using FlowOS.Workflows.Enums;

namespace FlowOS.UnitTests.Application.Services;

public class WorkflowClassCompiler_CapabilityTests
{
    [Fact]
    public void Compile_SynthesizesEventPublishCaps_WhenHumanTaskOmitsThem()
    {
        var workflowClass = new WorkflowClass(
            Guid.NewGuid(),
            "LegacyApproval",
            "1.0.0",
            new WorkflowClassBlueprint
            {
                Events =
                [
                    new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve", Category = EventCategory.Human }
                ],
                StateMachine = new StateMachineBlueprint
                {
                    InitialState = "Pending",
                    States = ["Pending", "Approved"],
                    Transitions =
                    [
                        new TransitionBlueprint { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" }
                    ]
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Review",
                    Steps =
                    [
                        new StepBlueprint
                        {
                            StepId = "Review",
                            StepType = "HumanTask",
                            RequiredRoles = ["Approver"],
                            NextSteps = new Dictionary<string, string> { ["EVT-APPROVE"] = "END" }
                        }
                    ]
                }
            });

        var definition = WorkflowClassCompiler.MapToRuntimeDefinition(workflowClass);
        var review = definition.Steps.Single(step => step.StepId == "Review");

        Assert.Equal(WorkflowStepType.HumanTask, review.StepType);
        Assert.Contains("event.publish.EVT-APPROVE", review.RequiredCapabilities);
        Assert.Contains("event.publish.EVT-APPROVE", review.EventRequiredCapabilities["EVT-APPROVE"]);
    }

    [Fact]
    public void ContextCompile_RemapsRequiredCapabilities()
    {
        var source = new WorkflowClass(
            Guid.NewGuid(),
            "Approval",
            "1.0.0",
            new WorkflowClassBlueprint
            {
                Events =
                [
                    new EventBlueprint
                    {
                        EventId = "EVT-APPROVE",
                        Name = "Approve",
                        Category = EventCategory.Human,
                        RequiredCapabilities = ["expense.approve"]
                    }
                ],
                StateMachine = new StateMachineBlueprint
                {
                    InitialState = "Pending",
                    States = ["Pending", "Approved"],
                    Transitions =
                    [
                        new TransitionBlueprint { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" }
                    ]
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Review",
                    Steps =
                    [
                        new StepBlueprint
                        {
                            StepId = "Review",
                            StepType = "HumanTask",
                            RequiredRoles = ["Approver"],
                            RequiredCapabilities = ["expense.approve"],
                            NextSteps = new Dictionary<string, string> { ["EVT-APPROVE"] = "END" }
                        }
                    ]
                }
            });
        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition
            {
                EntityType = "ExpenseEntity",
                EventAliases = new Dictionary<string, string> { ["EVT-APPROVE"] = "EVT-EXP-APPROVE" },
                RoleOverrides = new Dictionary<string, string> { ["Approver"] = "FinanceManager" },
                CapabilityOverrides = new Dictionary<string, string> { ["expense.approve"] = "finance.approve.v1" }
            });

        var package = WorkflowClassCompiler.MapToContextRuntimePackage(source, binding, revision);
        var review = package.WorkflowDefinition.Steps.Single(step => step.StepId == "Review");

        Assert.Contains("FinanceManager", review.AllowedRoles);
        Assert.Contains("finance.approve.v1", review.RequiredCapabilities);
        Assert.Contains("finance.approve.v1", review.EventRequiredCapabilities["EVT-EXP-APPROVE"]);
    }
}
