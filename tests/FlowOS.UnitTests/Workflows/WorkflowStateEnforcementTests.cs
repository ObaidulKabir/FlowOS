using System;
using System.Collections.Generic;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using FlowOS.Events.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Enums;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowStateEnforcementTests
{
    private readonly WorkflowEngine _engine;
    private readonly Guid _tenantId = Guid.NewGuid();

    public WorkflowStateEnforcementTests()
    {
        _engine = new WorkflowEngine(new StateMachineEngine());
    }

    [Fact]
    public void Advance_Should_Fail_If_StateMachine_Denies_Transition()
    {
        // 1. Setup Workflow
        var wfDef = new WorkflowDefinition(_tenantId, "OrderProcess", 1, "Start");
        wfDef.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command)
        {
            NextSteps = { { "Submit", "Review" } } // Workflow says: "Submit" event moves to "Review"
        });
        wfDef.AddStep(new WorkflowStepDefinition("Review", WorkflowStepType.HumanTask)); // Define Review step
        wfDef.Publish();

        var wfInstance = new WorkflowInstance(_tenantId, wfDef.Id, Guid.NewGuid(), 1, "Start");

        // 2. Setup State Machine (The Law)
        var smDef = new StateMachineDefinition(_tenantId, "Order", "Created");
        smDef.AddState("Created");
        smDef.AddState("Pending");
        smDef.AddState("Closed");
        smDef.AddTransition(new StateTransition("Created", "Pending", "Submit"));
        
        var currentEntityState = "Closed"; // Entity is closed
        
        // 3. Act
        var evt = new TestDomainEvent(_tenantId, "Submit");
        var result = _engine.Advance(
            wfInstance, 
            wfDef, 
            evt, 
            new FlowOS.StateMachines.Models.ExecutionContext(),
            smDef,
            currentEntityState
        );

        // 4. Assert
        Assert.False(result.Success);
        Assert.Contains("State Machine violation", result.Message);
        Assert.Contains("not valid for current state 'Closed'", result.Message);
    }

    [Fact]
    public void Advance_Should_Succeed_If_StateMachine_Allows_Transition()
    {
        // 1. Setup Workflow
        var wfDef = new WorkflowDefinition(_tenantId, "OrderProcess", 1);
        wfDef.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command)
        {
            NextSteps = { { "Submit", "Review" } }
        });
        wfDef.AddStep(new WorkflowStepDefinition("Review", WorkflowStepType.HumanTask));
        wfDef.Publish();

        var wfInstance = new WorkflowInstance(_tenantId, wfDef.Id, Guid.NewGuid(), 1, "Start");

        // 2. Setup State Machine
        var smDef = new StateMachineDefinition(_tenantId, "Order", "Created");
        smDef.AddState("Created");
        smDef.AddState("Pending");
        smDef.AddTransition(new StateTransition("Created", "Pending", "Submit"));
        
        var currentEntityState = "Created";

        // 3. Act
        var evt = new TestDomainEvent(_tenantId, "Submit");
        var result = _engine.Advance(
            wfInstance, 
            wfDef, 
            evt, 
            new FlowOS.StateMachines.Models.ExecutionContext(),
            smDef,
            currentEntityState
        );

        // 4. Assert
        Assert.True(result.Success);
        Assert.Equal("Review", wfInstance.CurrentStepId);
    }

    [Fact]
    public void Advance_DecisionAutoRoute_ThenAppliesQueuedStateEventWithoutWorkflowEdge()
    {
        var wfDef = new WorkflowDefinition(_tenantId, "ServiceRepair", 1, "IntakeRequest");
        wfDef.AddStep(new WorkflowStepDefinition("IntakeRequest", WorkflowStepType.HumanTask)
        {
            NextSteps = { { "JOB_REQUESTED", "ApproveQuote" } }
        });
        wfDef.AddStep(new WorkflowStepDefinition("ApproveQuote", WorkflowStepType.Decision)
        {
            Conditions = { { "Default", "MaterialDecision" } }
        });
        wfDef.AddStep(new WorkflowStepDefinition("MaterialDecision", WorkflowStepType.HumanTask)
        {
            NextSteps = { { "MATERIALS_REQUIRED", "CloseJob" } }
        });
        wfDef.AddStep(new WorkflowStepDefinition("CloseJob", WorkflowStepType.Command)
        {
            NextSteps = { { "Default", "END" } }
        });
        wfDef.Publish();

        var smDef = new StateMachineDefinition(_tenantId, "ServiceRepairJob", "Requested");
        smDef.AddState("Assigned");
        smDef.AddState("Quoted");
        smDef.AddState("RepairInProgress");
        smDef.AddTransition(new StateTransition("Requested", "Assigned", "JOB_REQUESTED"));
        smDef.AddTransition(new StateTransition("Assigned", "Quoted", "QUOTE_APPROVED"));
        smDef.AddTransition(new StateTransition("Quoted", "RepairInProgress", "MATERIALS_REQUIRED"));

        var instance = new WorkflowInstance(_tenantId, wfDef.Id, Guid.NewGuid(), 1, "IntakeRequest", initialState: "Requested");
        var context = new FlowOS.StateMachines.Models.ExecutionContext();

        var requested = _engine.Advance(instance, wfDef, new TestDomainEvent(_tenantId, "JOB_REQUESTED"), context, smDef, "Requested");
        Assert.True(requested.Success);
        Assert.Equal("MaterialDecision", instance.CurrentStepId);
        Assert.Equal("Assigned", instance.CurrentState);

        var quoted = _engine.Advance(instance, wfDef, new TestDomainEvent(_tenantId, "QUOTE_APPROVED"), context, smDef, instance.CurrentState);
        Assert.True(quoted.Success);
        Assert.Equal("MaterialDecision", instance.CurrentStepId);
        Assert.Equal("Quoted", instance.CurrentState);
        Assert.Contains("state-only event 'QUOTE_APPROVED'", quoted.Message);

        var materials = _engine.Advance(instance, wfDef, new TestDomainEvent(_tenantId, "MATERIALS_REQUIRED"), context, smDef, instance.CurrentState);
        Assert.True(materials.Success);
        Assert.Equal("CloseJob", instance.CurrentStepId);
        Assert.Equal("RepairInProgress", instance.CurrentState);
    }

    [Fact]
    public void Advance_MaterialsRequiredWhileStillAssigned_IsDeniedByStateMachine()
    {
        var wfDef = new WorkflowDefinition(_tenantId, "ServiceRepair", 1, "MaterialDecision");
        wfDef.AddStep(new WorkflowStepDefinition("MaterialDecision", WorkflowStepType.HumanTask)
        {
            NextSteps = { { "MATERIALS_REQUIRED", "CloseJob" } }
        });
        wfDef.AddStep(new WorkflowStepDefinition("CloseJob", WorkflowStepType.Command)
        {
            NextSteps = { { "Default", "END" } }
        });
        wfDef.Publish();

        var smDef = new StateMachineDefinition(_tenantId, "ServiceRepairJob", "Requested");
        smDef.AddState("Assigned");
        smDef.AddState("Quoted");
        smDef.AddState("RepairInProgress");
        smDef.AddTransition(new StateTransition("Assigned", "Quoted", "QUOTE_APPROVED"));
        smDef.AddTransition(new StateTransition("Quoted", "RepairInProgress", "MATERIALS_REQUIRED"));

        var instance = new WorkflowInstance(_tenantId, wfDef.Id, Guid.NewGuid(), 1, "MaterialDecision", initialState: "Assigned");
        var result = _engine.Advance(
            instance,
            wfDef,
            new TestDomainEvent(_tenantId, "MATERIALS_REQUIRED"),
            new FlowOS.StateMachines.Models.ExecutionContext(),
            smDef,
            "Assigned");

        Assert.False(result.Success);
        Assert.Contains("State Machine violation", result.Message);
        Assert.Equal("MaterialDecision", instance.CurrentStepId);
        Assert.Equal("Assigned", instance.CurrentState);
    }

    [Fact]
    public void Advance_SlaReminderWithoutNextSteps_StaysOnStep()
    {
        var wfDef = new WorkflowDefinition(_tenantId, "QuoteSla", 1, "ApproveQuote");
        wfDef.AddStep(new WorkflowStepDefinition("ApproveQuote", WorkflowStepType.HumanTask)
        {
            NextSteps = { { "QUOTE_APPROVED", "END" } },
            Sla = new StepSlaDefinition(
                "24h",
                "QUOTE_RESPONSE_OVERDUE",
                reminders: new List<StepReminderDefinition>
                {
                    new("2h", "QUOTE_REMINDER_SENT"),
                    new("12h", "QUOTE_REMINDER_SENT")
                })
        });
        wfDef.Publish();

        var smDef = new StateMachineDefinition(_tenantId, "ServiceRepairJob", "Assigned");
        smDef.AddState("Assigned");
        smDef.AddState("Quoted");
        smDef.AddTransition(new StateTransition("Assigned", "Quoted", "QUOTE_APPROVED"));

        var instance = new WorkflowInstance(_tenantId, wfDef.Id, Guid.NewGuid(), 1, "ApproveQuote", initialState: "Assigned");
        var result = _engine.Advance(
            instance,
            wfDef,
            new TestDomainEvent(_tenantId, "QUOTE_REMINDER_SENT"),
            new FlowOS.StateMachines.Models.ExecutionContext(),
            smDef,
            "Assigned");

        Assert.True(result.Success);
        Assert.Contains("[SLA Reminder Fired]", result.Message);
        Assert.Equal("ApproveQuote", instance.CurrentStepId);
        Assert.Equal("Assigned", instance.CurrentState);
    }

    public class TestDomainEvent : DomainEvent
    {
        public override string EventType { get; }
        public TestDomainEvent(Guid tenantId, string eventType) : base(tenantId, eventType)
        {
            EventType = eventType;
        }
    }
}
