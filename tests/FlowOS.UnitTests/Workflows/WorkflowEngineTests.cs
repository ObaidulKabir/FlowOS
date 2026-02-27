using FlowOS.StateMachines.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using FlowOS.UnitTests.Events; // Reusing TestDomainEvent
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowEngineTests
{
    private readonly WorkflowEngine _engine;
    private readonly Guid _tenantId = Guid.NewGuid();

    public WorkflowEngineTests()
    {
        _engine = new WorkflowEngine();
    }

    private WorkflowDefinition CreateApprovalWorkflow()
    {
        var def = new WorkflowDefinition(_tenantId, "Approval");

        // Step 1: Start -> Submit
        var step1 = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
        step1.NextSteps.Add("Submitted", "Review");
        def.AddStep(step1);

        // Step 2: Review (Human) -> Approved/Rejected
        var step2 = new WorkflowStepDefinition("Review", WorkflowStepType.HumanTask);
        step2.NextSteps.Add("Approved", "END");
        step2.NextSteps.Add("Rejected", "END");
        def.AddStep(step2);

        def.Publish();
        return def;
    }

    [Fact]
    public void Advance_ValidTransition_ShouldAdvance()
    {
        // Arrange
        var def = CreateApprovalWorkflow();
        var instance = new WorkflowInstance(_tenantId, def.Id, Guid.Empty, def.Version, def.StartStepId);
        var evt = new TestDomainEvent(_tenantId, "Submitted");

        // Act
        var result = _engine.Advance(instance, def, evt, new FlowOS.StateMachines.Models.ExecutionContext());

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Review", instance.CurrentStepId);
        Assert.Equal(WorkflowInstanceStatus.Waiting, instance.Status); // Human Task waits
    }

    [Fact]
    public void Advance_HumanTaskCompletion_ShouldComplete()
    {
        // Arrange
        var def = CreateApprovalWorkflow();
        var instance = new WorkflowInstance(_tenantId, def.Id, Guid.Empty, def.Version, "Review");
        var evt = new TestDomainEvent(_tenantId, "Approved");

        // Act
        var result = _engine.Advance(instance, def, evt, new FlowOS.StateMachines.Models.ExecutionContext());

        // Assert
        Assert.True(result.Success);
        Assert.Equal(WorkflowInstanceStatus.Completed, instance.Status);
    }

    [Fact]
    public void Advance_InvalidEvent_ShouldFail()
    {
        // Arrange
        var def = CreateApprovalWorkflow();
        var instance = new WorkflowInstance(_tenantId, def.Id, Guid.Empty, def.Version, "Start");
        var evt = new TestDomainEvent(_tenantId, "UnknownEvent");

        // Act
        var result = _engine.Advance(instance, def, evt, new FlowOS.StateMachines.Models.ExecutionContext());

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No transition defined", result.Message);
    }

    [Fact]
    public void Advance_DecisionStep_ShouldRouteBasedOnCondition()
    {
        // Arrange
        var def = new WorkflowDefinition(_tenantId, "DecisionFlow");

        // Start -> CheckAmount
        var step1 = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
        step1.NextSteps.Add("Submit", "CheckAmount");
        def.AddStep(step1);

        // CheckAmount (Decision) -> HighValue / LowValue
        var decision = new WorkflowStepDefinition("CheckAmount", WorkflowStepType.Decision);
        // Using simple "Key > Value" syntax supported by our naive evaluator
        decision.Conditions.Add("Amount > 100", "HighValue"); 
        decision.Conditions.Add("Amount <= 100", "LowValue");
        def.AddStep(decision);

        def.AddStep(new WorkflowStepDefinition("HighValue", WorkflowStepType.Command));
        def.AddStep(new WorkflowStepDefinition("LowValue", WorkflowStepType.Command));
        
        def.Publish();

        var instance = new WorkflowInstance(_tenantId, def.Id, Guid.Empty, def.Version, "Start");
        var evt = new TestDomainEvent(_tenantId, "Submit");
        
        // Context with Payload
        var context = new FlowOS.StateMachines.Models.ExecutionContext();
        context.Payload = new Dictionary<string, object> { { "Amount", 150 } };

        // Act
        var result = _engine.Advance(instance, def, evt, context);

        // Assert
        Assert.True(result.Success);
        // Should jump from Start -> CheckAmount -> HighValue
        Assert.Equal("HighValue", instance.CurrentStepId);
    }

    [Fact]
    public void Advance_DecisionStep_LowValue_ShouldRouteCorrectly()
    {
        // Arrange
        var def = new WorkflowDefinition(_tenantId, "DecisionFlow");
        var step1 = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
        step1.NextSteps.Add("Submit", "CheckAmount");
        def.AddStep(step1);

        var decision = new WorkflowStepDefinition("CheckAmount", WorkflowStepType.Decision);
        decision.Conditions.Add("Amount > 100", "HighValue"); 
        decision.Conditions.Add("Amount <= 100", "LowValue");
        def.AddStep(decision);

        def.AddStep(new WorkflowStepDefinition("HighValue", WorkflowStepType.Command));
        def.AddStep(new WorkflowStepDefinition("LowValue", WorkflowStepType.Command));
        def.Publish();

        var instance = new WorkflowInstance(_tenantId, def.Id, Guid.Empty, def.Version, "Start");
        var evt = new TestDomainEvent(_tenantId, "Submit");
        var context = new FlowOS.StateMachines.Models.ExecutionContext();
        context.Payload = new Dictionary<string, object> { { "Amount", 50 } };

        // Act
        _engine.Advance(instance, def, evt, context);

        // Assert
        Assert.Equal("LowValue", instance.CurrentStepId);
    }

    private class TestDomainEvent : FlowOS.Events.Models.DomainEvent
    {
        public TestDomainEvent(Guid tenantId, string eventType) : base(tenantId, eventType)
        {
        }
    }
}
