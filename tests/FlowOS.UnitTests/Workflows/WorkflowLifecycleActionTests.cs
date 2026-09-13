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

public class WorkflowLifecycleActionTests
{
    private readonly WorkflowClassValidator _validator = new();

    [Fact]
    public void Validator_ShouldReject_InvalidActionType()
    {
        var bp = CreateBaseBlueprint();
        bp.Workflow.Steps[0].OnEntry.Add(new StepActionBlueprint
        {
            ActionType = "InvalidType",
            Target = "https://example.com"
        });

        var wc = new WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", bp);
        var result = _validator.Validate(wc);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-ACT-001");
    }

    [Fact]
    public void Validator_ShouldReject_EmptyTarget()
    {
        var bp = CreateBaseBlueprint();
        bp.Workflow.Steps[0].OnExit.Add(new StepActionBlueprint
        {
            ActionType = "Notification",
            Target = ""
        });

        var wc = new WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", bp);
        var result = _validator.Validate(wc);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-ACT-003");
    }

    [Fact]
    public void Validator_ShouldReject_InvalidWebhookUrl()
    {
        var bp = CreateBaseBlueprint();
        bp.Workflow.Steps[0].OnEntry.Add(new StepActionBlueprint
        {
            ActionType = "Webhook",
            Target = "ftp://not-http-url.com"
        });

        var wc = new WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", bp);
        var result = _validator.Validate(wc);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-ACT-002");
    }

    [Fact]
    public void Validator_ShouldReject_PublishEventWithUnknownEventId()
    {
        var bp = CreateBaseBlueprint();
        bp.Workflow.Steps[0].OnExit.Add(new StepActionBlueprint
        {
            ActionType = "PublishEvent",
            Target = "NON_EXISTENT_EVENT"
        });

        var wc = new WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", bp);
        var result = _validator.Validate(wc);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-ACT-004");
    }

    [Fact]
    public void Validator_ShouldPass_WithValidActionHooks()
    {
        var bp = CreateBaseBlueprint();
        bp.Workflow.Steps[0].OnEntry.Add(new StepActionBlueprint
        {
            ActionType = "Webhook",
            Target = "https://api.example.com/notify",
            Condition = "Amount > 1000"
        });
        bp.Workflow.Steps[0].OnExit.Add(new StepActionBlueprint
        {
            ActionType = "Notification",
            Target = "Managers"
        });
        bp.Workflow.Steps[0].OnExit.Add(new StepActionBlueprint
        {
            ActionType = "PublishEvent",
            Target = "EVT-SUBMIT"
        });

        var wc = new WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", bp);
        var result = _validator.Validate(wc);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("WF-ACT-"));
    }

    [Fact]
    public void Compiler_ShouldMap_OnEntryAndOnExit_ToRuntimeDefinition()
    {
        var bp = CreateBaseBlueprint();
        bp.Workflow.Steps[0].OnEntry.Add(new StepActionBlueprint
        {
            ActionType = "Webhook",
            Target = "https://api.example.com/start",
            Condition = "Total > 50"
        });
        bp.Workflow.Steps[0].OnExit.Add(new StepActionBlueprint
        {
            ActionType = "Notification",
            Target = "OpsTeam"
        });

        var wc = new WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", bp);
        var compiled = WorkflowClassCompiler.MapToRuntimeDefinition(wc);

        var stepDef = compiled.Steps.First(s => s.StepId == "SubmitStep");
        Assert.Single(stepDef.OnEntry);
        Assert.Equal("Webhook", stepDef.OnEntry[0].ActionType);
        Assert.Equal("https://api.example.com/start", stepDef.OnEntry[0].Target);
        Assert.Equal("Total > 50", stepDef.OnEntry[0].Condition);

        Assert.Single(stepDef.OnExit);
        Assert.Equal("Notification", stepDef.OnExit[0].ActionType);
        Assert.Equal("OpsTeam", stepDef.OnExit[0].Target);
    }

    [Fact]
    public async Task Dispatcher_ShouldQueueOutbox_WhenConditionMatchesOrEmpty()
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
                Target = "https://api.example.com/order-approved",
                Condition = "Amount > 100"
            },
            new()
            {
                ActionType = "Notification",
                Target = "Managers",
                Condition = "Amount < 50" // Will not match
            },
            new()
            {
                ActionType = "Notification",
                Target = "Finance",
                Condition = "" // Empty condition always matches
            }
        };

        var payload = new Dictionary<string, object>
        {
            { "Amount", 500 }
        };

        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await dispatcher.QueueActionsAsync(tenantId, instanceId, "Step1", "OnEntry", actions, payload, CancellationToken.None);
        await db.SaveChangesAsync();

        var outboxMessages = await db.OutboxMessages.ToListAsync();
        Assert.Equal(2, outboxMessages.Count);
        Assert.Contains(outboxMessages, m => m.Type == "WorkflowAction:Webhook");
        Assert.Contains(outboxMessages, m => m.Type == "WorkflowAction:Notification");
    }

    [Fact]
    public async Task SimulationTools_ShouldTrace_LifecycleActions()
    {
        var mediatorMock = new Mock<IMediator>();
        var tools = new SimulationTools(mediatorMock.Object);

        var bp = CreateBaseBlueprint();
        bp.Workflow.Steps[0].OnEntry.Add(new StepActionBlueprint
        {
            ActionType = "Notification",
            Target = "Submitter",
            Condition = "Amount > 100"
        });
        bp.Workflow.Steps[0].OnExit.Add(new StepActionBlueprint
        {
            ActionType = "Webhook",
            Target = "https://example.com/log"
        });

        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(bp),
            ["payload"] = JObject.FromObject(new Dictionary<string, object>
            {
                { "Amount", 500 }
            }),
            ["events"] = new JArray("EVT-SUBMIT")
        };

        var result = await tools.SimulateWorkflowClass(args);
        Assert.False(result.IsError);

        var output = JObject.Parse(result.Content[0].Text);
        var data = output["data"] as JObject;
        Assert.NotNull(data);
        var actionsTriggered = data["actionsTriggered"] as JArray;
        Assert.NotNull(actionsTriggered);
        Assert.NotEmpty(actionsTriggered);

        Assert.Contains(actionsTriggered, a =>
            a["hook"]?.ToString() == "OnEntry" &&
            a["actionType"]?.ToString() == "Notification" &&
            a["status"]?.ToString() == "Executed");

        Assert.Contains(actionsTriggered, a =>
            a["hook"]?.ToString() == "OnExit" &&
            a["actionType"]?.ToString() == "Webhook" &&
            a["status"]?.ToString() == "Executed");
    }

    [Fact]
    public void ExpressionEvaluator_ShouldEvaluateValuesAndInterpolateTemplates()
    {
        var payload = new Dictionary<string, object>
        {
            { "Amount", 200 },
            { "Customer", "Alice" },
            { "OrderId", "ORD-99" }
        };

        // 1. EvaluateValue
        var arithmetic = FlowOS.StateMachines.Engine.ExpressionEvaluator.EvaluateValue("Amount * 1.5", payload);
        Assert.Equal(300.0, Convert.ToDouble(arithmetic));

        var directField = FlowOS.StateMachines.Engine.ExpressionEvaluator.EvaluateValue("Customer", payload);
        Assert.Equal("Alice", directField);

        var ternary = FlowOS.StateMachines.Engine.ExpressionEvaluator.EvaluateValue("Amount > 100 ? \"High\" : \"Low\"", payload);
        Assert.Equal("High", ternary);

        // 2. InterpolateTemplate
        var template = "Order {{OrderId}} for {{Customer}} total is ${{Amount * 2}}";
        var interpolated = FlowOS.StateMachines.Engine.ExpressionEvaluator.InterpolateTemplate(template, payload);
        Assert.Equal("Order ORD-99 for Alice total is $400", interpolated);

        // Unresolved identifier falls back to empty string in template
        var missingVarTemplate = "Hello {{UnknownVar}}!";
        var missingVarResult = FlowOS.StateMachines.Engine.ExpressionEvaluator.InterpolateTemplate(missingVarTemplate, payload);
        Assert.Equal("Hello !", missingVarResult);
    }

    [Fact]
    public async Task Dispatcher_ShouldInterpolateTemplatesAndTransformPayloadMapping()
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
                Target = "https://api.example.com/orders/{{OrderId}}",
                Url = "https://api.example.com/orders/{{OrderId}}",
                Template = "Dispatching order {{OrderId}} with amount ${{Amount}}",
                PayloadMapping = new Dictionary<string, string>
                {
                    { "externalOrderId", "OrderId" },
                    { "calculatedTotal", "Amount * 1.20" },
                    { "clientName", "Customer" },
                    { "staticFlag", "STATIC_VALUE" }
                }
            }
        };

        var payload = new Dictionary<string, object>
        {
            { "OrderId", "ORD-123" },
            { "Amount", 500 },
            { "Customer", "Acme Corp" }
        };

        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await dispatcher.QueueActionsAsync(tenantId, instanceId, "ShipStep", "OnEntry", actions, payload, CancellationToken.None);
        await db.SaveChangesAsync();

        var message = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "WorkflowAction:Webhook");
        Assert.NotNull(message);

        using var doc = System.Text.Json.JsonDocument.Parse(message.Payload);
        var root = doc.RootElement;

        Assert.Equal("https://api.example.com/orders/ORD-123", root.GetProperty("target").GetString());
        Assert.Equal("https://api.example.com/orders/ORD-123", root.GetProperty("url").GetString());
        Assert.Equal("Dispatching order ORD-123 with amount $500", root.GetProperty("template").GetString());

        var mappedPayload = root.GetProperty("payload");
        Assert.Equal("ORD-123", mappedPayload.GetProperty("externalOrderId").GetString());
        Assert.Equal(600.0, mappedPayload.GetProperty("calculatedTotal").GetDouble());
        Assert.Equal("Acme Corp", mappedPayload.GetProperty("clientName").GetString());
        Assert.Equal("STATIC_VALUE", mappedPayload.GetProperty("staticFlag").GetString());
    }

    [Fact]
    public async Task SimulationTools_ShouldRenderInterpolatedTemplateAndTransformedPayload_ForAI()
    {
        var mediatorMock = new Mock<IMediator>();
        var tools = new SimulationTools(mediatorMock.Object);

        var bp = CreateBaseBlueprint();
        bp.Workflow.Steps[0].OnEntry.Add(new StepActionBlueprint
        {
            ActionType = "Webhook",
            Target = "https://api.example.com/orders/{{OrderId}}",
            Template = "Order {{OrderId}} processed for customer {{Customer}}",
            PayloadMapping = new Dictionary<string, string>
            {
                { "referenceId", "OrderId" },
                { "discountedAmount", "Amount * 0.9" }
            },
            Condition = "Amount > 100"
        });

        var args = new JObject
        {
            ["blueprint"] = JObject.FromObject(bp),
            ["payload"] = JObject.FromObject(new Dictionary<string, object>
            {
                { "OrderId", "ORD-777" },
                { "Customer", "Bob Smith" },
                { "Amount", 1000 }
            }),
            ["events"] = new JArray("EVT-SUBMIT")
        };

        var result = await tools.SimulateWorkflowClass(args);
        Assert.False(result.IsError);

        var output = JObject.Parse(result.Content[0].Text);
        var data = output["data"] as JObject;
        Assert.NotNull(data);

        var actionsTriggered = data["actionsTriggered"] as JArray;
        Assert.NotNull(actionsTriggered);

        var webhookAction = actionsTriggered.FirstOrDefault(a => a["actionType"]?.ToString() == "Webhook");
        Assert.NotNull(webhookAction);

        Assert.Equal("https://api.example.com/orders/ORD-777", webhookAction["target"]?.ToString());
        Assert.Equal("Order ORD-777 processed for customer Bob Smith", webhookAction["interpolatedMessage"]?.ToString());

        var transformed = webhookAction["transformedPayload"] as JObject;
        Assert.NotNull(transformed);
        Assert.Equal("ORD-777", transformed["referenceId"]?.ToString());
        Assert.Equal(900.0, transformed["discountedAmount"]?.Value<double>());

        var executionTrace = data["executionTrace"] as JArray;
        Assert.NotNull(executionTrace);
        var actionTrace = executionTrace.FirstOrDefault(t => t["stepType"]?.ToString() == "LifecycleAction");
        Assert.NotNull(actionTrace);
        var actionDesc = actionTrace["action"]?.ToString();
        Assert.Contains("Rendered: \"Order ORD-777 processed for customer Bob Smith\"", actionDesc);
        Assert.Contains("Transformed Payload", actionDesc);
    }

    private static WorkflowClassBlueprint CreateBaseBlueprint()
    {
        return new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "EVT-SUBMIT", Name = "Submit" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = new List<string> { "Draft", "Submitted" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Draft", ToState = "Submitted", EventId = "EVT-SUBMIT" }
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
                        NextSteps = new Dictionary<string, string> { { "EVT-SUBMIT", "END" } }
                    }
                }
            }
        };
    }
}
