using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using FlowOS.StateMachines.Engine;
using FlowOS.StateMachines.Models;
using FlowOS.UnitTests.Events; // Reusing TestDomainEvent
using Xunit;

namespace FlowOS.UnitTests.StateMachines;

public class StateMachineEngineTests
{
    private readonly StateMachineEngine _engine;
    private readonly Guid _tenantId = Guid.NewGuid();

    public StateMachineEngineTests()
    {
        _engine = new StateMachineEngine();
    }

    private StateMachineDefinition CreateOrderWorkflow()
    {
        var def = new StateMachineDefinition(_tenantId, "Order", "Created");
        def.AddState("Created");
        def.AddState("Paid");
        def.AddState("Shipped");
        
        def.AddTransition(new StateTransition("Created", "Paid", "PaymentReceived"));
        def.AddTransition(new StateTransition("Paid", "Shipped", "OrderShipped"));
        
        return def;
    }

    [Fact]
    public void ValidateTransition_ValidTransition_ShouldBeAllowed()
    {
        // Arrange
        var def = CreateOrderWorkflow();
        var evt = new TestDomainEvent(_tenantId, "PaymentReceived");
        
        // Act
        var result = _engine.ValidateTransition(def, "Created", evt, new FlowOS.StateMachines.Models.ExecutionContext());

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal("Paid", result.MatchedTransition!.ToState);
    }

    [Fact]
    public void ValidateTransition_InvalidEvent_ShouldBeDenied()
    {
        // Arrange
        var def = CreateOrderWorkflow();
        var evt = new TestDomainEvent(_tenantId, "OrderShipped"); // Cannot ship from Created
        
        // Act
        var result = _engine.ValidateTransition(def, "Created", evt, new FlowOS.StateMachines.Models.ExecutionContext());

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("not valid for current state", result.Reason);
    }

    [Fact]
    public void ValidateTransition_UnknownState_ShouldBeDenied()
    {
        // Arrange
        var def = CreateOrderWorkflow();
        var evt = new TestDomainEvent(_tenantId, "PaymentReceived");
        
        // Act
        var result = _engine.ValidateTransition(def, "UnknownState", evt, new FlowOS.StateMachines.Models.ExecutionContext());

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("not valid", result.Reason);
    }

    private class TestDomainEvent : FlowOS.Events.Models.DomainEvent
    {
        public override string EventType { get; } 
        public TestDomainEvent(Guid tenantId, string eventType = "Test") : base(tenantId, eventType) 
        {
            EventType = eventType;
        }
    }
}
