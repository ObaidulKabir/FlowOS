using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowOS.Domain.Blueprints;
using FlowOS.MCP.Tools;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.MCP;

public class SimulationToolsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly SimulationTools _tools;

    public SimulationToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tools = new SimulationTools(_mediatorMock.Object);
    }

    private JObject CreateExpenseApprovalBlueprint()
    {
        return JObject.FromObject(new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "EVT-SUBMIT", Name = "Submit" },
                new() { EventId = "EVT-APPROVE", Name = "Approve" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = new List<string> { "Draft", "UnderReview", "Approved" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Draft", ToState = "UnderReview", EventId = "EVT-SUBMIT" },
                    new() { FromState = "UnderReview", ToState = "Approved", EventId = "EVT-APPROVE" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "SubmitStep",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "SubmitStep",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "EVT-SUBMIT", "CheckAmount" } }
                    },
                    new()
                    {
                        StepId = "CheckAmount",
                        StepType = "Decision",
                        Conditions = new Dictionary<string, string>
                        {
                            { "Amount > 5000", "DirectorApproval" },
                            { "Default", "ManagerApproval" }
                        }
                    },
                    new()
                    {
                        StepId = "DirectorApproval",
                        StepType = "HumanTask",
                        RequiredRoles = new List<string> { "Director" },
                        NextSteps = new Dictionary<string, string> { { "EVT-APPROVE", "Finalize" } }
                    },
                    new()
                    {
                        StepId = "ManagerApproval",
                        StepType = "HumanTask",
                        RequiredRoles = new List<string> { "Manager" },
                        NextSteps = new Dictionary<string, string> { { "EVT-APPROVE", "Finalize" } }
                    },
                    new()
                    {
                        StepId = "Finalize",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                    }
                }
            }
        });
    }

    [Fact]
    public async Task Simulate_InlineBlueprint_WithHighAmountAndDirectorRole_CompletesSuccessfully()
    {
        // Arrange
        var blueprint = CreateExpenseApprovalBlueprint();
        var args = new JObject
        {
            ["blueprint"] = blueprint,
            ["payload"] = new JObject
            {
                ["Amount"] = 7500,
                ["Department"] = "Engineering"
            },
            ["role"] = "Director",
            ["events"] = new JArray("EVT-APPROVE")
        };

        // Act
        var result = await _tools.SimulateWorkflowClass(args);

        // Assert
        Assert.False(result.IsError);
        var content = result.Content[0].Text;
        var response = JObject.Parse(content);
        Assert.True(response["ok"]?.Value<bool>());

        var data = response["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("Draft", data["initialState"]?.ToString());
        Assert.Equal("Approved", data["finalState"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());

        var decisions = data["decisionsEvaluated"] as JArray;
        Assert.NotNull(decisions);
        Assert.Single(decisions);
        Assert.True(decisions[0]["matched"]?.Value<bool>());
        Assert.Equal("DirectorApproval", decisions[0]["target"]?.ToString());
    }

    [Fact]
    public async Task Simulate_RoleMismatch_PausesAtHumanTask()
    {
        // Arrange
        var blueprint = CreateExpenseApprovalBlueprint();
        var args = new JObject
        {
            ["blueprint"] = blueprint,
            ["payload"] = new JObject { ["Amount"] = 8000 },
            ["role"] = "JuniorAccountant", // Not Director
            ["events"] = new JArray("EVT-APPROVE")
        };

        // Act
        var result = await _tools.SimulateWorkflowClass(args);

        // Assert
        Assert.False(result.IsError);
        var content = result.Content[0].Text;
        var response = JObject.Parse(content);
        var data = response["data"] as JObject;
        Assert.NotNull(data);

        Assert.Equal("WaitingForHumanTask", data["status"]?.ToString());
        Assert.Equal("DirectorApproval", data["currentStepId"]?.ToString());

        var pending = data["pendingHumanTask"] as JObject;
        Assert.NotNull(pending);
        Assert.True(pending["unauthorizedAttempt"]?.Value<bool>());
    }

    [Fact]
    public async Task Simulate_GuardConstraint_BlocksTransition()
    {
        // Arrange
        var blueprint = CreateExpenseApprovalBlueprint();
        // Add a guard condition on EVT-APPROVE transition: "ApprovedByBoard == true"
        var sm = (blueprint["StateMachine"] ?? blueprint["stateMachine"]) as JObject;
        var trans = (sm?["Transitions"] ?? sm?["transitions"]) as JArray;
        Assert.NotNull(trans);
        trans[1]["Condition"] = "ApprovedByBoard == true";

        var args = new JObject
        {
            ["blueprint"] = blueprint,
            ["payload"] = new JObject
            {
                ["Amount"] = 7500,
                ["ApprovedByBoard"] = false // Fails guard
            },
            ["role"] = "Director",
            ["events"] = new JArray("EVT-APPROVE")
        };

        // Act
        var result = await _tools.SimulateWorkflowClass(args);

        // Assert
        Assert.False(result.IsError);
        var content = result.Content[0].Text;
        var response = JObject.Parse(content);
        var data = response["data"] as JObject;
        Assert.NotNull(data);

        Assert.Equal("BlockedByGuard", data["status"]?.ToString());
        Assert.Equal("UnderReview", data["finalState"]?.ToString());
    }

    [Fact]
    public async Task Simulate_StuckAtDecision_WhenNoConditionMatchesAndNoDefault()
    {
        // Arrange
        var bp = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Gate",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "Gate",
                        StepType = "Decision",
                        Conditions = new Dictionary<string, string>
                        {
                            { "Amount > 100000", "SpecialPath" }
                        }
                    }
                }
            }
        };

        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(bp),
            ["payload"] = new JObject { ["Amount"] = 50 }
        };

        // Act
        var result = await _tools.SimulateWorkflowClass(args);

        // Assert
        Assert.False(result.IsError);
        var response = JObject.Parse(result.Content[0].Text);
        var data = response["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("StuckAtDecision", data["status"]?.ToString());
    }

    [Fact]
    public async Task Simulate_ById_FetchesFromMediatorAndSimulates()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var blueprintObj = CreateExpenseApprovalBlueprint();
        var blueprint = blueprintObj.ToObject<WorkflowClassBlueprint>()!;

        _mediatorMock.Setup(m => m.Send(
            It.Is<FlowOS.Application.Queries.Governance.GetWorkflowClassByIdQuery>(q => q.Id == id && q.TenantId == tenantId),
            default))
            .ReturnsAsync(new FlowOS.Application.DTOs.Governance.WorkflowClassResponseDto
            {
                Id = id,
                TenantId = tenantId,
                Name = "ExpenseApproval",
                Version = "1.0.0",
                Definition = blueprint
            });

        var args = new JObject
        {
            ["id"] = id.ToString(),
            ["tenantId"] = tenantId.ToString(),
            ["payload"] = new JObject { ["Amount"] = 3000 },
            ["role"] = "Manager",
            ["events"] = new JArray("EVT-APPROVE")
        };

        // Act
        var result = await _tools.SimulateWorkflowClass(args);

        // Assert
        Assert.False(result.IsError);
        var response = JObject.Parse(result.Content[0].Text);
        var data = response["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("ExpenseApproval v1.0.0", data["workflow"]?.ToString());
    }

    [Fact]
    public async Task SimulateWorkflowClass_WithInlineSubWorkflow_ExecutesChildAndMapsOutput()
    {
        // Arrange
        var parentBp = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "InvokeSub",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "InvokeSub",
                        StepType = "SubWorkflow",
                        SubWorkflow = new SubWorkflowReferenceBlueprint
                        {
                            WorkflowName = "TaxCalculationChild",
                            InputMapping = new Dictionary<string, string>
                            {
                                { "Income", "AnnualIncome" }
                            },
                            OutputMapping = new Dictionary<string, string>
                            {
                                { "CalculatedTax", "ChildTax" }
                            }
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "SubWorkflowCompleted", "END" }
                        }
                    }
                }
            }
        };

        var childBp = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Calculate",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "Calculate",
                        StepType = "Command",
                        OnExit = new List<StepActionBlueprint>
                        {
                            new()
                            {
                                ActionType = "Notification",
                                Target = "TaxService",
                                PayloadMapping = new Dictionary<string, string>
                                {
                                    { "ChildTax", "Income * 0.25" }
                                }
                            }
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "Default", "END" }
                        }
                    }
                }
            }
        };

        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(parentBp),
            ["subWorkflows"] = new JObject
            {
                ["TaxCalculationChild"] = JObject.FromObject(childBp)
            },
            ["payload"] = new JObject
            {
                ["AnnualIncome"] = 100000
            }
        };

        // Act
        var result = await _tools.SimulateWorkflowClass(args);

        // Assert
        Assert.False(result.IsError);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.True(data["payload"]?["SubWorkflowCompleted"]?.Value<bool>());
        Assert.Equal(25000.0, data["payload"]?["CalculatedTax"]?.Value<double>());

        var executed = data["subworkflowsExecuted"] as JArray;
        Assert.NotNull(executed);
        Assert.Single(executed);
        var childTrace = executed[0] as JObject;
        Assert.NotNull(childTrace);
        Assert.Equal("TaxCalculationChild", childTrace["childWorkflow"]?.ToString());
        Assert.Equal("Completed", childTrace["childStatus"]?.ToString());
    }

    [Fact]
    public async Task SimulateSubWorkflow_DedicatedTool_ExecutesChildAndReturnsSubworkflowTelemetry()
    {
        // Arrange
        var parentBp = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "VerificationStep",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "VerificationStep",
                        StepType = "SubWorkflow",
                        SubWorkflow = new SubWorkflowReferenceBlueprint
                        {
                            WorkflowName = "KycWorkflow",
                            InputMapping = new Dictionary<string, string>
                            {
                                { "DocId", "ApplicantDocId" }
                            },
                            OutputMapping = new Dictionary<string, string>
                            {
                                { "KycStatus", "VerifiedStatus" }
                            }
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "SubWorkflowCompleted", "END" }
                        }
                    }
                }
            }
        };

        var childBp = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "CheckDoc",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "CheckDoc",
                        StepType = "Command",
                        OnExit = new List<StepActionBlueprint>
                        {
                            new()
                            {
                                ActionType = "Notification",
                                Target = "Compliance",
                                PayloadMapping = new Dictionary<string, string>
                                {
                                    { "VerifiedStatus", "'PASS'" }
                                }
                            }
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "Default", "END" }
                        }
                    }
                }
            }
        };

        var args = new JObject
        {
            ["parentBlueprint"] = JObject.FromObject(parentBp),
            ["childBlueprint"] = JObject.FromObject(childBp),
            ["payload"] = new JObject
            {
                ["ApplicantDocId"] = "DOC-9988"
            }
        };

        // Act
        var result = await _tools.SimulateSubWorkflow(args);

        // Assert
        Assert.False(result.IsError);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("VerificationStep", data["subWorkflowStepId"]?.ToString());
        Assert.Equal("KycWorkflow", data["childWorkflow"]?.ToString());
        Assert.Equal("PASS", data["updatedParentPayload"]?["KycStatus"]?.ToString());
    }

    [Fact]
    public async Task SimulateSubWorkflow_ChildWaitingForHumanTask_PausesParentWithStructuredObject()
    {
        // Arrange
        var parentBp = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "RunSub",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "RunSub",
                        StepType = "SubWorkflow",
                        SubWorkflow = new SubWorkflowReferenceBlueprint
                        {
                            WorkflowName = "HumanChildWF"
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "SubWorkflowCompleted", "END" }
                        }
                    }
                }
            }
        };

        var childBp = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "NeedReview",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "NeedReview",
                        StepType = "HumanTask",
                        RequiredRoles = new List<string> { "Manager" },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "EVT-APPROVE", "END" }
                        }
                    }
                }
            }
        };

        var args = new JObject
        {
            ["parentBlueprint"] = JObject.FromObject(parentBp),
            ["childBlueprint"] = JObject.FromObject(childBp),
            ["role"] = "Manager" // No event passed for child HumanTask
        };

        // Act
        var result = await _tools.SimulateSubWorkflow(args);

        // Assert
        Assert.False(result.IsError);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("WaitingForSubWorkflow", data["status"]?.ToString());

        var pending = data["pendingSubWorkflow"] as JObject;
        Assert.NotNull(pending);
        Assert.Equal("RunSub", pending["stepId"]?.ToString());
        Assert.Equal("WaitingForHumanTask", pending["childStatus"]?.ToString());
    }

    [Fact]
    public async Task SimulateWorkflowClass_SubWorkflowWithoutChild_PausesWaitingForEvent()
    {
        // Arrange
        var parentBp = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "ExternalSub",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "ExternalSub",
                        StepType = "SubWorkflow",
                        NextSteps = new Dictionary<string, string>
                        {
                            { "EVT-EXTERNAL-DONE", "END" }
                        }
                    }
                }
            }
        };

        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(parentBp)
        };

        // Act
        var result = await _tools.SimulateWorkflowClass(args);

        // Assert
        Assert.False(result.IsError);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("WaitingForSubWorkflow", data["status"]?.ToString());
        var pending = data["pendingSubWorkflow"] as JObject;
        Assert.NotNull(pending);
        Assert.Equal("ExternalSub", pending["stepId"]?.ToString());
    }
}
