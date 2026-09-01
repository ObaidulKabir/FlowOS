using System;
using System.Collections.Generic;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;
using FlowOS.Infrastructure.Services;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowSlaAndEscalationTests
{
    private readonly WorkflowClassValidator _validator = new();
    private readonly WorkflowJsonLinter _linter = new();

    [Fact]
    public void Validator_Rejects_Sla_Without_Duration()
    {
        var bp = new WorkflowClassBlueprint
        {
            Events = new() { new EventBlueprint { EventId = "EVT-TIMEOUT", Name = "Timeout" } },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Approval",
                Steps = new()
                {
                    new StepBlueprint
                    {
                        StepId = "Approval",
                        StepType = "HumanTask",
                        Sla = new StepSlaBlueprint { Duration = "", TimeoutEvent = "EVT-TIMEOUT" },
                        NextSteps = new() { { "EVT-TIMEOUT", "END" } }
                    }
                }
            }
        };

        var wfClass = new FlowOS.Domain.Entities.WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", bp);
        var result = _validator.Validate(wfClass);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-SLA-001");
    }

    [Fact]
    public void Validator_Rejects_Sla_Without_TimeoutEvent()
    {
        var bp = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Approval",
                Steps = new()
                {
                    new StepBlueprint
                    {
                        StepId = "Approval",
                        StepType = "HumanTask",
                        Sla = new StepSlaBlueprint { Duration = "24h", TimeoutEvent = "" },
                        NextSteps = new() { { "Default", "END" } }
                    }
                }
            }
        };

        var wfClass = new FlowOS.Domain.Entities.WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", bp);
        var result = _validator.Validate(wfClass);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-SLA-002");
    }

    [Fact]
    public void Validator_Rejects_Sla_With_Unknown_EscalationStep()
    {
        var bp = new WorkflowClassBlueprint
        {
            Events = new() { new EventBlueprint { EventId = "EVT-TIMEOUT", Name = "Timeout" } },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Approval",
                Steps = new()
                {
                    new StepBlueprint
                    {
                        StepId = "Approval",
                        StepType = "HumanTask",
                        Sla = new StepSlaBlueprint { Duration = "24h", TimeoutEvent = "EVT-TIMEOUT", EscalationStepId = "NonExistentStep" },
                        NextSteps = new() { { "EVT-TIMEOUT", "END" } }
                    }
                }
            }
        };

        var wfClass = new FlowOS.Domain.Entities.WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", bp);
        var result = _validator.Validate(wfClass);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CON-004");
    }

    [Fact]
    public void Linter_Validates_Declarative_Sla_Syntax()
    {
        var json = """
        {
          "events": [
            { "eventId": "EVT-TIMEOUT", "name": "Timeout", "category": "System" }
          ],
          "stateMachine": {
            "states": ["Pending", "Escalated"],
            "transitions": [
              { "fromState": "Pending", "toState": "Escalated", "eventId": "EVT-TIMEOUT" }
            ]
          },
          "workflow": {
            "startStepId": "TaskStep",
            "steps": [
              {
                "stepId": "TaskStep",
                "stepType": "HumanTask",
                "sla": {
                  "duration": "10s",
                  "timeoutEvent": "EVT-TIMEOUT"
                },
                "nextSteps": {
                  "EVT-TIMEOUT": "END"
                }
              }
            ]
          }
        }
        """;

        var errors = _linter.Lint(json);
        Assert.Empty(errors);
    }
}
