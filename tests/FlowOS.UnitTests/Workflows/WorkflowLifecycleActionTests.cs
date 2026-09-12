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
        Assert.Empty(result.Errors.Where(e => e.Code.StartsWith("WF-ACT-")));
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
