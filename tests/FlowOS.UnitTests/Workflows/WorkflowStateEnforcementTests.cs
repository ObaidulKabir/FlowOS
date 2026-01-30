using System;
using System.Collections.Generic;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using FlowOS.Events.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowStateEnforcementTests
{
    private readonly WorkflowEngine _engine;
    private readonly Guid _tenantId = Guid.NewGuid();

    public WorkflowStateEnforcementTests()
    {
        _engine = new WorkflowEngine();
    }

    [Fact]
    public void Advance_Should_Fail_If_StateMachine_Denies_Transition()
    {
        // 1. Setup Workflow
        var wfDef = new WorkflowDefinition(_tenantId, "OrderProcess", 1);
        wfDef.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command)
        {
            NextSteps = { { "Submit", "Review" } } // Workflow says: "Submit" event moves to "Review"
        });
        wfDef.AddStep(new WorkflowStepDefinition("Review", WorkflowStepType.HumanTask)); // Define Review step
        wfDef.Publish();

        var wfInstance = new WorkflowInstance(_tenantId, wfDef.Id, 1, "Start", Guid.NewGuid());

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

        var wfInstance = new WorkflowInstance(_tenantId, wfDef.Id, 1, "Start", Guid.NewGuid());

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

    public class TestDomainEvent : DomainEvent
    {
        public override string EventType { get; }
        public TestDomainEvent(Guid tenantId, string eventType) : base(tenantId, eventType)
        {
            EventType = eventType;
        }
    }
}
