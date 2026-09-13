using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.MCP.Tools;
using FlowOS.Workflows.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowSagaCompensationTests
{
    private readonly WorkflowClassValidator _validator = new();

    private static WorkflowClassBlueprint CreateBaseBlueprint()
    {
        return new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "EVT-SUBMIT", Name = "Submit" },
                new() { EventId = "EVT-APPROVE", Name = "Approve" },
                new() { EventId = "EVT-REJECT", Name = "Reject" },
                new() { EventId = "EVT-COMPENSATE", Name = "Compensate" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = new List<string> { "Draft", "Processing", "Completed", "Failed" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Draft", ToState = "Processing", EventId = "EVT-SUBMIT" },
                    new() { FromState = "Processing", ToState = "Completed", EventId = "EVT-APPROVE" },
                    new() { FromState = "Processing", ToState = "Failed", EventId = "EVT-REJECT" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Step1",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "Step1",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "Default", "Step2" } }
                    },
                    new()
                    {
                        StepId = "Step2",
                        StepType = "HumanTask",
                        NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                    }
                }
            }
        };
    }

    [Fact]
    public void Validator_ShouldValidate_OnFailureActions()
    {
        var bp = CreateBaseBlueprint();
        // Add valid OnFailure compensating action
        bp.Workflow.Steps[0].OnFailure.Add(new StepActionBlueprint
        {
            ActionType = "Webhook",
            Target = "https://api.payment.com/refund",
            PayloadMapping = new Dictionary<string, string> { { "refundId", "PaymentId" } }
        });

        var wc = new WorkflowClass(Guid.NewGuid(), "SagaWorkflow", "1.0.0", bp);
        var result = _validator.Validate(wc);
        Assert.True(result.IsValid);

        // Add invalid OnFailure action (invalid target)
        bp.Workflow.Steps[0].OnFailure.Add(new StepActionBlueprint
        {
            ActionType = "Webhook",
            Target = "ftp://invalid-url"
        });

        var invalidWc = new WorkflowClass(Guid.NewGuid(), "SagaWorkflowInvalid", "1.0.0", bp);
        var invalidResult = _validator.Validate(invalidWc);
        Assert.False(invalidResult.IsValid);
        Assert.Contains(invalidResult.Errors, e => e.Code == "WF-ACT-002");
    }

    [Fact]
    public void Compiler_ShouldMap_OnFailure_ToRuntimeDefinition()
    {
        var bp = CreateBaseBlueprint();
        bp.Workflow.Steps[0].OnFailure.Add(new StepActionBlueprint
        {
            ActionType = "Webhook",
            Target = "https://inventory.service/release-lock",
            Condition = "HoldInventory == true",
            PayloadMapping = new Dictionary<string, string>
            {
                { "sku", "ItemSku" },
                { "quantity", "Qty" }
            }
        });

        var wc = new WorkflowClass(Guid.NewGuid(), "CompilerSagaWorkflow", "1.0.0", bp);
        var compiled = WorkflowClassCompiler.MapToRuntimeDefinition(wc);

        var stepDef = compiled.Steps.First(s => s.StepId == "Step1");
        Assert.Single(stepDef.OnFailure);
        Assert.Equal("Webhook", stepDef.OnFailure[0].ActionType);
        Assert.Equal("https://inventory.service/release-lock", stepDef.OnFailure[0].Target);
        Assert.Equal("HoldInventory == true", stepDef.OnFailure[0].Condition);
        Assert.Equal("ItemSku", stepDef.OnFailure[0].PayloadMapping["sku"]);
    }

    [Fact]
    public async Task Dispatcher_ShouldQueueOutbox_ForOnFailureCompensatingActions()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var dispatcher = new WorkflowActionDispatcher(db);

        var actions = new List<StepActionDefinition>
        {
            new()
            {
                ActionType = "Webhook",
                Target = "https://billing.example.com/refund",
                PayloadMapping = new Dictionary<string, string>
                {
                    { "chargeId", "TxId" },
                    { "refundAmount", "Amount" }
                }
            },
            new()
            {
                ActionType = "Notification",
                Target = "DevOpsOnCall",
                Template = "Step execution failed for Tx {{TxId}}"
            }
        };

        var payload = new Dictionary<string, object>
        {
            { "TxId", "TX-9988" },
            { "Amount", 350.00 }
        };

        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await dispatcher.QueueActionsAsync(tenantId, instanceId, "ChargeCustomerStep", "OnFailure", actions, payload, CancellationToken.None);
        await db.SaveChangesAsync();

        var outboxMessages = await db.OutboxMessages.ToListAsync();
        Assert.Equal(2, outboxMessages.Count);

        var webhookMsg = outboxMessages.FirstOrDefault(m => m.Type == "WorkflowAction:Webhook");
        Assert.NotNull(webhookMsg);
        using var doc = System.Text.Json.JsonDocument.Parse(webhookMsg.Payload);
        Assert.Equal("OnFailure", doc.RootElement.GetProperty("triggerPhase").GetString());
        Assert.Equal("https://billing.example.com/refund", doc.RootElement.GetProperty("target").GetString());
    }

    [Fact]
    public async Task LifecycleActionMcpTools_ShouldSupport_OnFailureHook()
    {
        var mediatorMock = new Mock<IMediator>();
        var validator = new WorkflowClassValidator();
        var tools = new LifecycleActionMcpTools(mediatorMock.Object, validator);

        var bp = CreateBaseBlueprint();

        // 1. Attach OnFailure compensation action
        var attachArgs = new JObject
        {
            ["blueprint"] = JObject.FromObject(bp),
            ["stepId"] = "Step1",
            ["hook"] = "OnFailure",
            ["action"] = new JObject
            {
                ["actionType"] = "Webhook",
                ["url"] = "https://api.gateway.com/rollback/{{TransactionId}}",
                ["template"] = "Rollback triggered for {{TransactionId}}",
                ["signPayload"] = true
            }
        };

        var attachResult = await tools.AttachStepAction(attachArgs);
        Assert.False(attachResult.IsError);

        var attachOutput = JObject.Parse(attachResult.Content[0].Text);
        var updatedBp = attachOutput["data"]?["blueprint"]?.ToObject<WorkflowClassBlueprint>();
        Assert.NotNull(updatedBp);
        Assert.Single(updatedBp.Workflow.Steps[0].OnFailure);
        Assert.Equal("Webhook", updatedBp.Workflow.Steps[0].OnFailure[0].ActionType);

        // 2. List actions with hook: "OnFailure"
        var listArgs = new JObject
        {
            ["blueprint"] = JObject.FromObject(updatedBp),
            ["stepId"] = "Step1",
            ["hook"] = "OnFailure"
        };

        var listResult = await tools.ListStepActions(listArgs);
        Assert.False(listResult.IsError);
        var listOutput = JObject.Parse(listResult.Content[0].Text);
        var steps = listOutput["data"]?["steps"] as JArray;
        Assert.NotNull(steps);
        Assert.Single(steps);

        var stepObj = steps[0] as JObject;
        Assert.NotNull(stepObj);
        var onFailureActions = stepObj["onFailure"] as JArray;
        Assert.NotNull(onFailureActions);
        Assert.Single(onFailureActions);

        // 3. Remove action
        var removeArgs = new JObject
        {
            ["blueprint"] = JObject.FromObject(updatedBp),
            ["stepId"] = "Step1",
            ["hook"] = "OnFailure",
            ["actionIndex"] = 0
        };

        var removeResult = await tools.RemoveStepAction(removeArgs);
        Assert.False(removeResult.IsError);
        var removeOutput = JObject.Parse(removeResult.Content[0].Text);
        var postRemoveBp = removeOutput["data"]?["blueprint"]?.ToObject<WorkflowClassBlueprint>();
        Assert.NotNull(postRemoveBp);
        Assert.Empty(postRemoveBp.Workflow.Steps[0].OnFailure);
    }

    [Fact]
    public async Task SimulationTools_ShouldExecuteOnFailureCompensation_WhenFailureSimulated()
    {
        var mediatorMock = new Mock<IMediator>();
        var tools = new SimulationTools(mediatorMock.Object);

        var bp = CreateBaseBlueprint();
        // Add OnEntry hook
        bp.Workflow.Steps[0].OnEntry.Add(new StepActionBlueprint
        {
            ActionType = "Notification",
            Target = "System",
            Template = "Starting step 1"
        });

        // Add OnFailure rollback/compensation hook
        bp.Workflow.Steps[0].OnFailure.Add(new StepActionBlueprint
        {
            ActionType = "Webhook",
            Target = "https://api.payment.com/cancel-hold/{{HoldId}}",
            Template = "Compensating step failure by releasing hold {{HoldId}}"
        });

        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(bp),
            ["payload"] = JObject.FromObject(new Dictionary<string, object>
            {
                { "HoldId", "HOLD-456" }
            }),
            ["events"] = new JArray("EVT-SUBMIT"),
            ["simulateFailureAtStep"] = "Step1"
        };

        var result = await tools.SimulateWorkflowClass(args);
        Assert.False(result.IsError);

        var output = JObject.Parse(result.Content[0].Text);
        var data = output["data"] as JObject;
        Assert.NotNull(data);

        var actionsTriggered = data["actionsTriggered"] as JArray;
        Assert.NotNull(actionsTriggered);

        // OnFailure action should be triggered with hook "OnFailure" and status "Executed"
        var failureCompAction = actionsTriggered.FirstOrDefault(a => a["hook"]?.ToString() == "OnFailure");
        Assert.NotNull(failureCompAction);
        Assert.Equal("Executed", failureCompAction["status"]?.ToString());
        Assert.Equal("https://api.payment.com/cancel-hold/HOLD-456", failureCompAction["target"]?.ToString());
        Assert.Equal("Compensating step failure by releasing hold HOLD-456", failureCompAction["interpolatedMessage"]?.ToString());

        // Trace should contain simulated failure and lifecycle action
        var trace = data["executionTrace"] as JArray;
        Assert.NotNull(trace);
        Assert.Contains(trace, t => t["action"]?.ToString()?.Contains("[Saga Failure Injected]") == true);
        Assert.Contains(trace, t => t["action"]?.ToString()?.Contains("[Hook OnFailure]") == true);
    }

    [Fact]
    public async Task SimulationTools_ShouldPlanCompensationPath_InReverseExecutionOrder()
    {
        var mediatorMock = new Mock<IMediator>();
        var compensationPlanner = new CompensationPlannerService();
        var tools = new SimulationTools(mediatorMock.Object, compensationPlanner);

        var bp = CreateBaseBlueprint();
        bp.Workflow.Steps[0].OnFailure.Add(new StepActionBlueprint
        {
            ActionType = "Webhook",
            Target = "https://example.com/undo-step1"
        });
        bp.Workflow.Steps[1].OnFailure.Add(new StepActionBlueprint
        {
            ActionType = "Notification",
            Target = "Ops"
        });

        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(bp),
            ["failedStepId"] = "Step2",
            ["executedStepIds"] = new JArray("Step1", "Step2")
        };

        var result = await tools.SimulateCompensationPath(args);
        Assert.False(result.IsError);

        var output = JObject.Parse(result.Content[0].Text);
        var data = output["data"] as JObject;
        Assert.NotNull(data);
        Assert.True(data["isFullyCompensable"]?.Value<bool>());

        var ordered = data["orderedCompensations"] as JArray;
        Assert.NotNull(ordered);
        Assert.Equal("Step2", ordered[0]?["stepId"]?.ToString());
        Assert.Equal("Step1", ordered[1]?["stepId"]?.ToString());
    }
}
