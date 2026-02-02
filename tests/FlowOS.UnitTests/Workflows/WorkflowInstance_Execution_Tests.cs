using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects; // Added for StateTransition
using FlowOS.StateMachines.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowInstance_Execution_Tests
{
    private readonly WorkflowEngine _engine;
    private readonly Guid _tenantId = Guid.NewGuid();

    public WorkflowInstance_Execution_Tests()
    {
        _engine = new WorkflowEngine();
    }

    [Fact]
    public void StartInstance_FromPublishedWorkflowClass_ShouldInitializeCorrectly()
    {
        // 1. Governance: Create & Publish WorkflowClass
        var bp = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit" },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve" }
            },
            StateMachine = new StateMachineBlueprint 
            { 
                InitialState = "Draft", 
                States = new List<string> { "Draft", "Submitted" } 
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Init",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint 
                    { 
                        StepId = "Init", 
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "EVT-SUBMIT", "Review" } }
                    },
                    new StepBlueprint 
                    { 
                        StepId = "Review", 
                        StepType = "HumanTask",
                        NextSteps = new Dictionary<string, string> { { "Default", "END" } } // Added exit path to fix WF-COMP-002
                    }
                }
            }
        };

        var wc = new WorkflowClass(_tenantId, "ExpenseRequest", "1.0.0", bp);
        wc.Publish();

        // 2. Runtime: Convert to Definition (Simulating Loader/Mapper)
        var def = MapToDefinition(wc);

        // 3. Execution: Create Instance
        var instance = new WorkflowInstance(_tenantId, def.Id, wc.Id, def.Version, def.StartStepId);

        // Assert
        Assert.Equal(WorkflowClassStatus.Published, wc.Status);
        Assert.Equal("Init", instance.CurrentStepId);
        Assert.Equal(WorkflowInstanceStatus.Running, instance.Status);
        Assert.Equal(wc.Id, instance.WorkflowClassId);
    }

    [Fact]
    public void ExecuteStep_ValidEvent_ShouldTransition()
    {
        // Arrange
        var (wc, def) = CreatePublishedWorkflow();
        var instance = new WorkflowInstance(_tenantId, def.Id, wc.Id, def.Version, "Init");
        var evt = new TestDomainEvent(_tenantId, "EVT-SUBMIT");

        // Act
        var result = _engine.Advance(instance, def, evt, new FlowOS.StateMachines.Models.ExecutionContext());

        // Assert
        Assert.True(result.Success, $"Advance failed: {result.Message}");
        Assert.Equal("Review", instance.CurrentStepId);
        Assert.Equal(WorkflowInstanceStatus.Waiting, instance.Status); // HumanTask -> Waiting
    }

    [Fact]
    public void ExecuteStep_InvalidEvent_ShouldNotTransition()
    {
        // Arrange
        var (wc, def) = CreatePublishedWorkflow();
        var instance = new WorkflowInstance(_tenantId, def.Id, wc.Id, def.Version, "Init");
        var evt = new TestDomainEvent(_tenantId, "EVT-UNKNOWN");

        // Act
        var result = _engine.Advance(instance, def, evt, new FlowOS.StateMachines.Models.ExecutionContext());

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Init", instance.CurrentStepId); // State preserved
        Assert.Contains("No transition defined", result.Message);
    }

    [Fact]
    public void ExecuteStep_PolicyDenial_ShouldBlockExecution_AndPreserveState()
    {
        // Arrange
        var (wc, def) = CreatePublishedWorkflow();
        var instance = new WorkflowInstance(_tenantId, def.Id, wc.Id, def.Version, "Init");
        var evt = new TestDomainEvent(_tenantId, "EVT-SUBMIT");

        // Setup State Machine (Law) that DENIES this transition
        // e.g., Entity is in "Locked" state, preventing "Draft" -> "Submitted"
        var smDef = new StateMachineDefinition(_tenantId, "Expense", "Draft");
        smDef.AddState("Draft");
        smDef.AddState("Submitted");
        // Transition exists but logic might fail or we simulate invalid current state
        smDef.AddTransition(new StateTransition("Draft", "Submitted", "EVT-SUBMIT"));

        // If current entity state is "Archived", the transition "Draft"->"Submitted" is invalid (Assuming current state must match FromState)
        var currentEntityState = "Archived"; 

        // Act
        var result = _engine.Advance(
            instance, 
            def, 
            evt, 
            new FlowOS.StateMachines.Models.ExecutionContext(), 
            smDef, 
            currentEntityState
        );

        // Assert
        Assert.False(result.Success);
        Assert.Contains("State Machine violation", result.Message);
        Assert.Equal("Init", instance.CurrentStepId); // Workflow state must NOT change
    }

    [Fact]
    public void InstanceRecovery_AfterRestart_ShouldResumeCorrectly()
    {
        // Arrange
        var (wc, def) = CreatePublishedWorkflow();
        
        // 1. Run Initial
        // Simulate a running instance that is at "Review" step
        var instance = new WorkflowInstance(_tenantId, def.Id, wc.Id, def.Version, "Init");
        instance.AdvanceTo("Review"); 
        instance.Wait();
        
        // Snapshot state (Simulate Persistence)
        var recoveredStepId = instance.CurrentStepId;
        
        // 2. Simulate Restart / Recovery
        // We create a "recovered" instance. 
        // Since we can't easily inject state into a new object without reflection or internal constructor,
        // we will use the same object but verify that the ENGINE treats it purely based on its public state properties.
        // The engine is stateless regarding the instance.
        
        // Act: Try to advance from the recovered state
        var evt = new TestDomainEvent(_tenantId, "EVT-APPROVE");
        
        // The engine should respect the current state "Review" and transition to "Done"
        var result = _engine.Advance(instance, def, evt, new FlowOS.StateMachines.Models.ExecutionContext());

        // Assert
        Assert.True(result.Success, "Failed to resume workflow from recovered state");
        Assert.Equal("Done", instance.CurrentStepId);
        Assert.Equal(WorkflowInstanceStatus.Running, instance.Status); // "Done" is an Event step (auto-running) or Command
        
        // Verify we actually moved from the recovered step
        Assert.NotEqual(recoveredStepId, instance.CurrentStepId);
    }
    
    // --- Helpers ---

    private (WorkflowClass, WorkflowDefinition) CreatePublishedWorkflow()
    {
        var bp = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit" },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve" }
            },
            StateMachine = new StateMachineBlueprint 
            { 
                InitialState = "Draft", 
                States = new List<string> { "Draft", "Submitted", "Approved" } 
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Init",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint 
                    { 
                        StepId = "Init", 
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "EVT-SUBMIT", "Review" } }
                    },
                    new StepBlueprint 
                    { 
                        StepId = "Review", 
                        StepType = "HumanTask",
                        NextSteps = new Dictionary<string, string> { { "EVT-APPROVE", "Done" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "Done",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                    }
                }
            }
        };

        var wc = new WorkflowClass(_tenantId, "RecoveryFlow", "1.0.0", bp);
        wc.Publish();
        var def = MapToDefinition(wc);
        return (wc, def);
    }

    private WorkflowDefinition MapToDefinition(WorkflowClass wc)
    {
        // Simple mapper for test purposes
        // In real app, this would be a robust service
        var def = new WorkflowDefinition(wc.TenantId, wc.Name, 1, wc.Definition.Workflow.StartStepId);
        
        // Since we can't modify WorkflowDefinition after creation (it's Draft by default in constructor),
        // we add steps then Publish.
        
        foreach (var stepBp in wc.Definition.Workflow.Steps)
        {
            var type = Enum.Parse<WorkflowStepType>(stepBp.StepType);
            var stepDef = new WorkflowStepDefinition(stepBp.StepId, type);
            foreach (var kvp in stepBp.NextSteps)
            {
                stepDef.NextSteps.Add(kvp.Key, kvp.Value);
            }
            def.AddStep(stepDef);
        }
        
        def.Publish();
        return def;
    }

    private class TestDomainEvent : FlowOS.Events.Models.DomainEvent
    {
        public override string EventType { get; }
        public TestDomainEvent(Guid tenantId, string eventType) : base(tenantId, eventType)
        {
            EventType = eventType;
        }
    }
}
