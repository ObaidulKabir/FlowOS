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
}
