using System;
using System.Collections.Generic;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;

namespace FlowOS.UnitTests.Domain;

public class WorkflowClassValidator_Tests
{
    private static WorkflowClassBlueprint CreateValidBlueprint()
    {
        return new WorkflowClassBlueprint
        {
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Created",
                States = new List<string> { "Created", "Done" }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Start",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint
                    {
                        StepId = "Start",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                    }
                }
            }
        };
    }

    private static WorkflowClass CreateWorkflowClass(WorkflowClassBlueprint? blueprint = null)
    {
        return new WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", blueprint ?? CreateValidBlueprint());
    }

    [Fact]
    public void Validate_ValidWorkflowClass_ReturnsNoErrors()
    {
        var validator = new WorkflowClassValidator();
        var workflowClass = CreateWorkflowClass();

        var result = validator.Validate(workflowClass);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MissingInitialState_ReturnsWorkflowStructureError()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateValidBlueprint() with
        {
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "",
                States = new List<string> { "Created", "Done" }
            }
        };

        var result = validator.Validate(CreateWorkflowClass(blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-STR-001");
    }

    [Fact]
    public void Validate_WorkflowWithoutSteps_ReturnsWorkflowStructureError()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateValidBlueprint() with
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Start",
                Steps = new List<StepBlueprint>()
            }
        };

        var result = validator.Validate(CreateWorkflowClass(blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-STR-002");
    }

    [Fact]
    public void Validate_StepWithoutType_ReturnsWorkflowStructureError()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateValidBlueprint() with
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Start",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint
                    {
                        StepId = "Start",
                        StepType = "",
                        NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                    }
                }
            }
        };

        var result = validator.Validate(CreateWorkflowClass(blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-STR-004");
    }

    [Fact]
    public void Validate_TransitionWithUndeclaredEvent_ReturnsConsistencyError()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateValidBlueprint() with
        {
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Created",
                States = new List<string> { "Created", "Done" },
                Transitions = new List<TransitionBlueprint>
                {
                    new TransitionBlueprint { FromState = "Created", ToState = "Done", EventId = "EVT-MISSING" }
                }
            }
        };

        var result = validator.Validate(CreateWorkflowClass(blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CON-001");
    }

    [Fact]
    public void Validate_TransitionWithUnknownFromState_ReturnsConsistencyError()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateValidBlueprint() with
        {
            Events = new List<EventBlueprint> { new EventBlueprint { EventId = "EVT-DONE", Name = "Done" } },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Created",
                States = new List<string> { "Created", "Done" },
                Transitions = new List<TransitionBlueprint>
                {
                    new TransitionBlueprint { FromState = "Missing", ToState = "Done", EventId = "EVT-DONE" }
                }
            }
        };

        var result = validator.Validate(CreateWorkflowClass(blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CON-002");
    }

    [Fact]
    public void Validate_StartStepReferencingUnknownStep_ReturnsCompletenessError()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateValidBlueprint() with
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "MissingStep",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint
                    {
                        StepId = "Start",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                    }
                }
            }
        };

        var result = validator.Validate(CreateWorkflowClass(blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-COMP-001");
    }

    [Fact]
    public void Validate_DecisionWithoutConditions_ReturnsDecisionErrors()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateValidBlueprint() with
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Decide",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint
                    {
                        StepId = "Decide",
                        StepType = "Decision",
                        Conditions = new Dictionary<string, string>(),
                        NextSteps = new Dictionary<string, string>()
                    }
                }
            }
        };

        var result = validator.Validate(CreateWorkflowClass(blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-COMP-002");
        Assert.Contains(result.Errors, e => e.Code == "WF-VAL-001");
    }

    [Fact]
    public void Validate_HumanTaskWithoutExitPath_ReturnsStepValidationError()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateValidBlueprint() with
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Review",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint
                    {
                        StepId = "Review",
                        StepType = "HumanTask",
                        NextSteps = new Dictionary<string, string>(),
                        Conditions = new Dictionary<string, string>()
                    }
                }
            }
        };

        var result = validator.Validate(CreateWorkflowClass(blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-VAL-002");
    }

    [Fact]
    public void Validate_DecisionConditionWithUnknownTarget_ReturnsConsistencyError()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateValidBlueprint() with
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Decide",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint
                    {
                        StepId = "Decide",
                        StepType = "Decision",
                        Conditions = new Dictionary<string, string> { { "True", "MissingStep" } }
                    }
                }
            }
        };

        var result = validator.Validate(CreateWorkflowClass(blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CON-004" && e.Message.Contains("Conditions"));
    }

    [Fact]
    public void Validate_RoleGrantingUndeclaredCapability_ReturnsGovernanceError()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateValidBlueprint() with
        {
            Roles = new List<RoleBlueprint>
            {
                new RoleBlueprint
                {
                    Name = "Approver",
                    GrantedCapabilities = new List<string> { "workflow.approve" }
                }
            },
            Capabilities = new List<CapabilityBlueprint>()
        };

        var result = validator.Validate(CreateWorkflowClass(blueprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "GOV-001");
    }
}
