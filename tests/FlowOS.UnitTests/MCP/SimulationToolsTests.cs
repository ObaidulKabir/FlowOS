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

    [Fact]
    public async Task SimulateWorkflowClass_RelativeTimerStep_ResolvesScheduleAndReturnsPendingTimer()
    {
        // Arrange: Blueprint 1 with a pre-event lead time timer
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "EVT-REMIND-24H", Name = "24h Pre-Event Reminder" },
                new() { EventId = "EVT-CHECKIN", Name = "Check In" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Booked",
                States = new List<string> { "Booked", "ReminderSent", "CheckedIn" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Booked", ToState = "ReminderSent", EventId = "EVT-REMIND-24H" },
                    new() { FromState = "ReminderSent", ToState = "CheckedIn", EventId = "EVT-CHECKIN" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "WaitForLeadTime",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "WaitForLeadTime",
                        StepType = "Timer",
                        Conditions = new Dictionary<string, string>
                        {
                            { "targetTimestampProperty", "appointmentDate" },
                            { "leadTime", "-24h" }
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "EVT-REMIND-24H", "SendPreEventAlert" }
                        }
                    },
                    new()
                    {
                        StepId = "SendPreEventAlert",
                        StepType = "Command",
                        OnEntry = new List<StepActionBlueprint>
                        {
                            new()
                            {
                                ActionType = "Notification",
                                Target = "Patient",
                                Template = "Reminder: Your appointment is in 24 hours."
                            }
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "Default", "AwaitCheckIn" }
                        }
                    },
                    new()
                    {
                        StepId = "AwaitCheckIn",
                        StepType = "HumanTask",
                        RequiredRoles = new List<string> { "Patient" },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "EVT-CHECKIN", "END" }
                        }
                    }
                }
            }
        };

        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["payload"] = new JObject
            {
                ["appointmentDate"] = "2026-10-01T14:00:00Z"
            }
        };

        // Act
        var result = await _tools.SimulateWorkflowClass(args);

        // Assert
        Assert.False(result.IsError);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("WaitingForTimer", data["status"]?.ToString());

        var pendingTimer = data["pendingTimer"] as JObject;
        Assert.NotNull(pendingTimer);
        Assert.Equal("WaitForLeadTime", pendingTimer["stepId"]?.ToString());
        Assert.Equal("Relative", pendingTimer["timerType"]?.ToString());
        Assert.Equal("appointmentDate", pendingTimer["targetProperty"]?.ToString());
        Assert.Equal("-24h", pendingTimer["offset"]?.ToString());
        Assert.Equal(DateTime.Parse("2026-09-30T14:00:00Z").ToUniversalTime(), pendingTimer["dueTimeUtc"]!.Value<DateTime>().ToUniversalTime());

        var allowedEvents = pendingTimer["allowedEvents"] as JArray;
        Assert.NotNull(allowedEvents);
        Assert.Contains(allowedEvents, e => e.ToString() == "EVT-REMIND-24H");
    }

    [Fact]
    public async Task SimulateWorkflowClass_RelativeTimerStep_WithAutoAdvanceTimers_ElapsesAndReachesEnd()
    {
        // Arrange: Blueprint with Timer and autoAdvanceTimers enabled
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "EVT-REMIND-24H", Name = "24h Pre-Event Reminder" },
                new() { EventId = "EVT-CHECKIN", Name = "Check In" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Booked",
                States = new List<string> { "Booked", "ReminderSent", "CheckedIn" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Booked", ToState = "ReminderSent", EventId = "EVT-REMIND-24H" },
                    new() { FromState = "ReminderSent", ToState = "CheckedIn", EventId = "EVT-CHECKIN" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "WaitForLeadTime",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "WaitForLeadTime",
                        StepType = "Timer",
                        Conditions = new Dictionary<string, string>
                        {
                            { "targetTimestampProperty", "appointmentDate" },
                            { "leadTime", "-24h" }
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "EVT-REMIND-24H", "SendPreEventAlert" }
                        }
                    },
                    new()
                    {
                        StepId = "SendPreEventAlert",
                        StepType = "Command",
                        OnEntry = new List<StepActionBlueprint>
                        {
                            new()
                            {
                                ActionType = "Notification",
                                Target = "Patient",
                                Template = "Your appointment is tomorrow at 2:00 PM."
                            }
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "Default", "AwaitCheckIn" }
                        }
                    },
                    new()
                    {
                        StepId = "AwaitCheckIn",
                        StepType = "HumanTask",
                        RequiredRoles = new List<string> { "Patient" },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "EVT-CHECKIN", "END" }
                        }
                    }
                }
            }
        };

        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["payload"] = new JObject
            {
                ["appointmentDate"] = "2026-10-01T14:00:00Z"
            },
            ["role"] = "Patient",
            ["autoAdvanceTimers"] = true,
            ["events"] = new JArray { "EVT-CHECKIN" }
        };

        // Act
        var result = await _tools.SimulateWorkflowClass(args);

        // Assert
        Assert.False(result.IsError);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("CheckedIn", data["finalState"]?.ToString());

        var actions = data["actionsTriggered"] as JArray;
        Assert.NotNull(actions);
        Assert.Contains(actions, a => a["actionType"]?.ToString() == "Notification" && a["stepId"]?.ToString() == "SendPreEventAlert");
    }

    [Fact]
    public async Task SimulateWorkflowClass_HumanTaskWithSlaReminders_SimulatesReminderLoopbackAndApproval()
    {
        // Arrange: Blueprint 2 with SLA multi-tier reminders and loopback transition
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "EVT-WARN-24H", Name = "24h Warning" },
                new() { EventId = "EVT-APPROVE", Name = "Approved" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "PendingApproval",
                States = new List<string> { "PendingApproval", "Approved" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "PendingApproval", ToState = "PendingApproval", EventId = "EVT-WARN-24H" },
                    new() { FromState = "PendingApproval", ToState = "Approved", EventId = "EVT-APPROVE" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "ManagerReview",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "ManagerReview",
                        StepType = "HumanTask",
                        RequiredRoles = new List<string> { "FinanceManager" },
                        Sla = new StepSlaBlueprint
                        {
                            Duration = "48h",
                            TimeoutEvent = "EVT-ESCALATE",
                            Reminders = new List<StepReminderBlueprint>
                            {
                                new() { Duration = "-24h", TriggerEvent = "EVT-WARN-24H" }
                            }
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "EVT-WARN-24H", "ManagerReview" },
                            { "EVT-APPROVE", "END" }
                        }
                    }
                }
            }
        };

        // 1. First test: Pausing without events exposes reminders in pendingHumanTask
        var pauseArgs = new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["role"] = "FinanceManager"
        };
        var pauseResult = await _tools.SimulateWorkflowClass(pauseArgs);
        Assert.False(pauseResult.IsError);
        var pauseData = JObject.Parse(pauseResult.Content[0].Text)["data"] as JObject;
        Assert.NotNull(pauseData);
        Assert.Equal("WaitingForHumanTask", pauseData["status"]?.ToString());

        var pendingTask = pauseData["pendingHumanTask"] as JObject;
        Assert.NotNull(pendingTask);
        var reminders = pendingTask["reminders"] as JArray;
        Assert.NotNull(reminders);
        Assert.Single(reminders);
        Assert.Equal("-24h", reminders[0]["duration"]?.ToString());
        Assert.Equal("EVT-WARN-24H", reminders[0]["triggerEvent"]?.ToString());

        // 2. Second test: Dispatches intermediate reminder loopback event then approval event
        var runArgs = new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["role"] = "FinanceManager",
            ["events"] = new JArray { "EVT-WARN-24H", "EVT-APPROVE" }
        };
        var runResult = await _tools.SimulateWorkflowClass(runArgs);
        Assert.False(runResult.IsError);
        var runData = JObject.Parse(runResult.Content[0].Text)["data"] as JObject;
        Assert.NotNull(runData);
        Assert.Equal("Completed", runData["status"]?.ToString());
        Assert.Equal("Approved", runData["finalState"]?.ToString());

        var trace = runData["executionTrace"] as JArray;
        Assert.NotNull(trace);
        Assert.Contains(trace, t => t["action"]?.ToString()?.Contains("[SLA Reminder Fired]") == true);
    }

    [Fact]
    public async Task SimulateWorkflowClass_HumanTaskSla_FiresRemindersBeforeCompletingEventWithoutQueuingThem()
    {
        var blueprint = CreateQuoteSlaBlueprint(includeTimeoutNextStep: false);
        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["role"] = "Customer",
            ["events"] = new JArray { "QUOTE_APPROVED" }
        };

        var result = await _tools.SimulateWorkflowClass(args);
        Assert.False(result.IsError);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("Quoted", data["finalState"]?.ToString());

        var trace = data["executionTrace"] as JArray;
        Assert.NotNull(trace);
        var reminderActions = trace
            .Select(t => t["action"]?.ToString() ?? "")
            .Where(action => action.Contains("[SLA Reminder Fired]"))
            .ToList();
        Assert.Equal(2, reminderActions.Count);
        Assert.Contains(reminderActions, action => action.Contains("QUOTE_REMINDER_SENT"));
        Assert.DoesNotContain(trace, t => t["action"]?.ToString()?.Contains("[SLA Timeout Fired]") == true);
    }

    [Fact]
    public async Task SimulateWorkflowClass_HumanTaskSla_AutoAdvanceTimersFiresTimeoutWithoutLiveInstance()
    {
        var blueprint = CreateQuoteSlaBlueprint(includeTimeoutNextStep: true);
        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["role"] = "Customer",
            ["autoAdvanceTimers"] = true
        };

        var result = await _tools.SimulateWorkflowClass(args);
        Assert.False(result.IsError);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("Overdue", data["finalState"]?.ToString());

        var trace = data["executionTrace"] as JArray;
        Assert.NotNull(trace);
        Assert.Equal(2, trace.Count(t => t["action"]?.ToString()?.Contains("[SLA Reminder Fired]") == true));
        Assert.Contains(trace, t => t["action"]?.ToString()?.Contains("[SLA Timeout Fired]") == true);
        Assert.Contains(trace, t => t["action"]?.ToString()?.Contains("QUOTE_RESPONSE_OVERDUE") == true);
    }

    [Fact]
    public async Task SimulateWorkflowClass_CommandSla_FiresRemindersBeforeQuoteApproved()
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "QUOTE_REMINDER_SENT" },
                new() { EventId = "QUOTE_APPROVED" },
                new() { EventId = "QUOTE_RESPONSE_OVERDUE" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Assigned",
                States = new List<string> { "Assigned", "Quoted" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Assigned", ToState = "Quoted", EventId = "QUOTE_APPROVED" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "ApproveQuote",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "ApproveQuote",
                        StepType = "Command",
                        Sla = new StepSlaBlueprint
                        {
                            Duration = "24h",
                            TimeoutEvent = "QUOTE_RESPONSE_OVERDUE",
                            Reminders = new List<StepReminderBlueprint>
                            {
                                new() { Duration = "2h", TriggerEvent = "QUOTE_REMINDER_SENT" },
                                new() { Duration = "12h", TriggerEvent = "QUOTE_REMINDER_SENT" }
                            }
                        },
                        NextSteps = new Dictionary<string, string>
                        {
                            { "QUOTE_APPROVED", "END" },
                            { "QUOTE_RESPONSE_OVERDUE", "END" }
                        }
                    }
                }
            }
        };

        var result = await _tools.SimulateWorkflowClass(new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["events"] = new JArray { "QUOTE_APPROVED" }
        });
        Assert.False(result.IsError);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("Quoted", data["finalState"]?.ToString());
        var trace = data["executionTrace"] as JArray;
        Assert.NotNull(trace);
        Assert.Equal(2, trace.Count(t => t["action"]?.ToString()?.Contains("[SLA Reminder Fired]") == true));
    }

    private static WorkflowClassBlueprint CreateQuoteSlaBlueprint(bool includeTimeoutNextStep)
    {
        var nextSteps = new Dictionary<string, string>
        {
            { "QUOTE_APPROVED", "END" }
        };
        if (includeTimeoutNextStep)
            nextSteps["QUOTE_RESPONSE_OVERDUE"] = "END";

        return new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "QUOTE_REMINDER_SENT" },
                new() { EventId = "QUOTE_APPROVED" },
                new() { EventId = "QUOTE_RESPONSE_OVERDUE" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Assigned",
                States = new List<string> { "Assigned", "Quoted", "Overdue" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Assigned", ToState = "Quoted", EventId = "QUOTE_APPROVED" },
                    new() { FromState = "Assigned", ToState = "Overdue", EventId = "QUOTE_RESPONSE_OVERDUE" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "ApproveQuote",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "ApproveQuote",
                        StepType = "HumanTask",
                        RequiredRoles = new List<string> { "Customer" },
                        Sla = new StepSlaBlueprint
                        {
                            Duration = "24h",
                            TimeoutEvent = "QUOTE_RESPONSE_OVERDUE",
                            Reminders = new List<StepReminderBlueprint>
                            {
                                new() { Duration = "2h", TriggerEvent = "QUOTE_REMINDER_SENT" },
                                new() { Duration = "12h", TriggerEvent = "QUOTE_REMINDER_SENT" }
                            }
                        },
                        NextSteps = nextSteps
                    }
                }
            }
        };
    }

    [Fact]
    public async Task Simulate_DecisionAndDefaultCommand_ApplyQueuedSystemStateEvents()
    {
        var blueprint = JObject.FromObject(new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "JOB_REQUESTED" },
                new() { EventId = "QUOTE_APPROVED" },
                new() { EventId = "MATERIALS_SKIPPED" },
                new() { EventId = "REPAIR_COMPLETED" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Requested",
                States = new List<string> { "Requested", "Assigned", "Quoted", "RepairInProgress", "Completed" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Requested", ToState = "Assigned", EventId = "JOB_REQUESTED" },
                    new() { FromState = "Assigned", ToState = "Quoted", EventId = "QUOTE_APPROVED" },
                    new() { FromState = "Quoted", ToState = "RepairInProgress", EventId = "MATERIALS_SKIPPED" },
                    new() { FromState = "RepairInProgress", ToState = "Completed", EventId = "REPAIR_COMPLETED" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "IntakeRequest",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "IntakeRequest",
                        StepType = "HumanTask",
                        RequiredRoles = new List<string> { "provider" },
                        NextSteps = new Dictionary<string, string> { { "JOB_REQUESTED", "ApproveQuote" } }
                    },
                    new()
                    {
                        StepId = "ApproveQuote",
                        StepType = "Decision",
                        Conditions = new Dictionary<string, string> { { "Default", "MaterialDecision" } }
                    },
                    new()
                    {
                        StepId = "MaterialDecision",
                        StepType = "Decision",
                        Conditions = new Dictionary<string, string> { { "Default", "ExecuteRepair" } }
                    },
                    new()
                    {
                        StepId = "ExecuteRepair",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "Default", "CloseJob" } }
                    },
                    new()
                    {
                        StepId = "CloseJob",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                    }
                }
            }
        });

        var args = new JObject
        {
            ["blueprint"] = blueprint,
            ["role"] = "provider",
            ["events"] = new JArray("JOB_REQUESTED", "QUOTE_APPROVED", "MATERIALS_SKIPPED", "REPAIR_COMPLETED")
        };

        var result = await _tools.SimulateWorkflowClass(args);
        Assert.False(result.IsError);
        var data = JObject.Parse(result.Content[0].Text)["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.Equal("Requested", data["initialState"]?.ToString());
        Assert.Equal("Completed", data["finalState"]?.ToString());

        var transitions = data["stateTransitions"] as JArray;
        Assert.NotNull(transitions);
        Assert.Equal(4, transitions.Count);
        Assert.Equal("Assigned", transitions[0]["to"]?.ToString());
        Assert.Equal("Quoted", transitions[1]["to"]?.ToString());
        Assert.Equal("RepairInProgress", transitions[2]["to"]?.ToString());
        Assert.Equal("Completed", transitions[3]["to"]?.ToString());

        var remaining = data["eventsRemaining"] as JArray;
        Assert.NotNull(remaining);
        Assert.Empty(remaining);
    }
}
