using System.Text.Json;
using FlowOS.Application.Services;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Workflows;

/// <summary>
/// Locks the quote-alert failure: a SystemTask continues only on the exact nextSteps key
/// "Default", a known-but-illegal Default is a state-machine violation, and notification
/// text is template. An empty url hides the stored recipient.
/// </summary>
public class AlertTimerChainTests
{
    private readonly WorkflowEngine _engine = new(new StateMachineEngine());
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public void SystemTask_WithExactDefault_EntersTheNextWait()
    {
        var (instance, definition) = QuoteChain(alertNextEvent: "Default");

        var trace = WorkflowAutoAdvanceRunner.Run(_engine, instance, definition, _tenantId, new FlowOS.StateMachines.Models.ExecutionContext());

        Assert.Equal("WaitTier2", instance.CurrentStepId);
        Assert.Single(trace);
        Assert.True(trace[0].IsAllowed);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("SystemContinue")]
    public void SystemTask_WithAnyOtherNextStepKey_StaysOnTheAlert(string nextEvent)
    {
        var (instance, definition) = QuoteChain(alertNextEvent: nextEvent);

        var trace = WorkflowAutoAdvanceRunner.Run(_engine, instance, definition, _tenantId, new FlowOS.StateMachines.Models.ExecutionContext());

        Assert.Equal("Alert1", instance.CurrentStepId);
        Assert.Empty(trace);
    }

    [Fact]
    public void HumanTask_WithDefault_DoesNotAutoAdvance()
    {
        var (instance, definition) = QuoteChain(alertNextEvent: "Default", alertType: WorkflowStepType.HumanTask);

        var trace = WorkflowAutoAdvanceRunner.Run(_engine, instance, definition, _tenantId, new FlowOS.StateMachines.Models.ExecutionContext());

        Assert.Equal("Alert1", instance.CurrentStepId);
        Assert.Empty(trace);
    }

    [Fact]
    public void TimerStep_WithDefault_DoesNotSkipTheWait()
    {
        var (instance, definition) = QuoteChain(alertNextEvent: "Default", alertType: WorkflowStepType.Timer);

        WorkflowAutoAdvanceRunner.Run(_engine, instance, definition, _tenantId, new FlowOS.StateMachines.Models.ExecutionContext());

        Assert.Equal("Alert1", instance.CurrentStepId);
    }

    [Fact]
    public void Command_WithExactDefault_EntersTheNextWait()
    {
        var (instance, definition) = QuoteChain(alertNextEvent: "Default", alertType: WorkflowStepType.Command);

        WorkflowAutoAdvanceRunner.Run(_engine, instance, definition, _tenantId, new FlowOS.StateMachines.Models.ExecutionContext());

        Assert.Equal("WaitTier2", instance.CurrentStepId);
    }

    [Fact]
    public void StateMachine_ThatNeverDeclaresDefault_StillEntersTheNextWaitAndKeepsItsState()
    {
        var (instance, definition) = QuoteChain(alertNextEvent: "Default", initialState: "Alert1Active");
        var machine = QuoteMachine(allowDefault: false);

        var trace = WorkflowAutoAdvanceRunner.Run(
            _engine, instance, definition, _tenantId, new FlowOS.StateMachines.Models.ExecutionContext(), machine);

        Assert.Equal("WaitTier2", instance.CurrentStepId);
        Assert.Equal("Alert1Active", instance.CurrentState);
        Assert.True(trace[0].IsAllowed);
    }

    [Fact]
    public void StateMachine_ThatKnowsDefaultFromAnotherState_BlocksTheAlert()
    {
        var (instance, definition) = QuoteChain(alertNextEvent: "Default", initialState: "Alert1Active");
        var machine = QuoteMachine(allowDefault: false);
        machine.AddState("Created");
        machine.AddTransition(new StateTransition("Created", "Alert1Active", "Default"));

        var trace = WorkflowAutoAdvanceRunner.Run(
            _engine, instance, definition, _tenantId, new FlowOS.StateMachines.Models.ExecutionContext(), machine);

        Assert.Equal("Alert1", instance.CurrentStepId);
        Assert.Equal("Alert1Active", instance.CurrentState);
        Assert.False(trace[0].IsAllowed);
        Assert.Contains("State Machine violation", trace[0].Message);
    }

    [Fact]
    public void StateMachine_WithDefault_EntersTheNextTimerStep()
    {
        var (instance, definition) = QuoteChain(alertNextEvent: "Default", initialState: "Alert1Active");
        var machine = QuoteMachine(allowDefault: true);

        var trace = WorkflowAutoAdvanceRunner.Run(
            _engine, instance, definition, _tenantId, new FlowOS.StateMachines.Models.ExecutionContext(), machine);

        Assert.Equal("WaitTier2", instance.CurrentStepId);
        Assert.Equal("WaitTier2Active", instance.CurrentState);
        Assert.True(trace[0].IsAllowed);
    }

    [Fact]
    public async Task Notification_ShowsTemplate_WhileEmptyUrlHidesTheRecipient()
    {
        var queued = await QueueNotificationAsync(template: "Alert: Quote not viewed", target: "Sales");

        Assert.Equal("Sales", queued.StoredTarget);
        Assert.Equal("Alert: Quote not viewed", queued.StoredTemplate);
        Assert.Equal(string.Empty, queued.StoredUrl);
        Assert.Equal("[Alert1] Alert: Quote not viewed (Target: )", queued.Message);
    }

    [Fact]
    public async Task Notification_SentenceStoredOnlyInTarget_RendersABlankBody()
    {
        var queued = await QueueNotificationAsync(template: null, target: "Alert: Quote not viewed");

        Assert.Equal("Alert: Quote not viewed", queued.StoredTarget);
        Assert.Equal(string.Empty, queued.StoredTemplate);
        Assert.Equal("[Alert1]  (Target: )", queued.Message);
        Assert.DoesNotContain("Quote not viewed", queued.Message);
    }

    [Fact]
    public void AgentOutline_StatesTheDefaultKey_AndTheTemplateRule()
    {
        var instructions = FlowOsMcpGuidance.SystemInstructions;
        var blueprint = McpToolSchemas.BlueprintSchema().ToString();
        var attach = McpToolSchemas.AttachStepAction().ToString();

        Assert.Contains("exact key `Default`", instructions);
        Assert.Contains("never declares event `Default`", instructions);
        Assert.Contains("skips the wait immediately", instructions);
        Assert.Contains("trace stops on the alert", instructions);
        Assert.Contains("Notification text is `template`", instructions);
        Assert.Contains("next tier is a separate SLA", blueprint);
        Assert.Contains("exact key Default", blueprint);
        Assert.Contains("Not the message text.", blueprint);
        Assert.Contains("Not the notification sentence.", attach);
        Assert.Contains("Notification message body.", attach);
    }

    private async Task<(string Message, string? StoredTarget, string? StoredTemplate, string? StoredUrl)> QueueNotificationAsync(string? template, string target)
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new FlowOSDbContext(options);
        var dispatcher = new WorkflowActionDispatcher(db);
        await dispatcher.QueueActionsAsync(
            _tenantId,
            Guid.NewGuid(),
            "Alert1",
            "OnEntry",
            new List<StepActionDefinition>
            {
                new()
                {
                    ActionType = "Notification",
                    Target = target,
                    Template = template
                }
            },
            new Dictionary<string, object>(),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var stored = await db.OutboxMessages.SingleAsync(m => m.Type == "WorkflowAction:Notification");
        using var doc = JsonDocument.Parse(stored.Payload);
        var root = doc.RootElement;
        return (
            WorkflowNotificationText.Format(root),
            root.GetProperty("target").GetString(),
            root.GetProperty("template").GetString(),
            root.GetProperty("url").GetString());
    }

    private (WorkflowInstance Instance, WorkflowDefinition Definition) QuoteChain(
        string alertNextEvent,
        WorkflowStepType alertType = WorkflowStepType.SystemTask,
        string? initialState = null)
    {
        var definition = new WorkflowDefinition(_tenantId, "QuoteAlerts", 1, "Alert1");
        var alert = new WorkflowStepDefinition("Alert1", alertType)
        {
            NextSteps = new Dictionary<string, string> { [alertNextEvent] = "WaitTier2" }
        };
        var wait = new WorkflowStepDefinition("WaitTier2", WorkflowStepType.HumanTask)
        {
            NextSteps = new Dictionary<string, string> { ["T2Done"] = "Alert2" }
        };
        definition.AddStep(alert);
        definition.AddStep(wait);

        var instance = new WorkflowInstance(_tenantId, definition.Id, Guid.Empty, 1, "Alert1", initialState: initialState);
        return (instance, definition);
    }

    private StateMachineDefinition QuoteMachine(bool allowDefault)
    {
        var machine = new StateMachineDefinition(_tenantId, "Quote", "Alert1Active");
        machine.AddState("WaitTier2Active");
        machine.AddTransition(new StateTransition("Alert1Active", "WaitTier2Active", "T1Done"));
        if (allowDefault)
            machine.AddTransition(new StateTransition("Alert1Active", "WaitTier2Active", "Default"));
        return machine;
    }
}
