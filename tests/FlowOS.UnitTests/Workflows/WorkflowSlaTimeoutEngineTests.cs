using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

/// <summary>
/// Engine behaviour for SLA timeout events (introduced in 22b82f8): when a step's SLA
/// <c>TimeoutEvent</c> fires and the step has no explicit <c>NextSteps</c> edge for it,
/// the instance moves to the SLA's <c>EscalationStepId</c>.
/// </summary>
public class WorkflowSlaTimeoutEngineTests
{
    private const string Timeout = "APPROVAL_OVERDUE";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly WorkflowEngine _engine = new(new StateMachineEngine());

    private WorkflowDefinition Definition(string? escalationStepId, bool explicitTimeoutEdge = false)
    {
        var definition = new WorkflowDefinition(_tenantId, "SlaTimeout");

        var review = new WorkflowStepDefinition("Start", WorkflowStepType.HumanTask)
        {
            Sla = new StepSlaDefinition("24h", Timeout, escalationStepId)
        };
        review.NextSteps.Add("Approved", "END");
        if (explicitTimeoutEdge)
            review.NextSteps.Add(Timeout, "ExplicitTimeoutStep");
        definition.AddStep(review);

        var escalation = new WorkflowStepDefinition("ManagerEscalation", WorkflowStepType.HumanTask);
        escalation.NextSteps.Add("Approved", "END");
        definition.AddStep(escalation);

        var explicitStep = new WorkflowStepDefinition("ExplicitTimeoutStep", WorkflowStepType.HumanTask);
        explicitStep.NextSteps.Add("Approved", "END");
        definition.AddStep(explicitStep);

        definition.Publish();
        return definition;
    }

    private WorkflowAdvanceResult Fire(WorkflowDefinition definition, WorkflowInstance instance, string eventType) =>
        _engine.Advance(instance, definition, new TestDomainEvent(_tenantId, eventType), new FlowOS.StateMachines.Models.ExecutionContext());

    private WorkflowInstance Instance(WorkflowDefinition definition) =>
        new(_tenantId, definition.Id, Guid.Empty, definition.Version, definition.StartStepId);

    [Fact]
    public void Timeout_moves_to_the_escalation_step()
    {
        var definition = Definition("ManagerEscalation");
        var instance = Instance(definition);

        var result = Fire(definition, instance, Timeout);

        Assert.True(result.Success, result.Message);
        Assert.Equal("ManagerEscalation", instance.CurrentStepId);
    }

    [Fact]
    public void Timeout_with_END_as_escalation_completes_the_instance()
    {
        var definition = Definition("END");
        var instance = Instance(definition);

        var result = Fire(definition, instance, Timeout);

        Assert.True(result.Success, result.Message);
        Assert.Equal(WorkflowInstanceStatus.Completed, instance.Status);
    }

    [Fact]
    public void Timeout_without_an_escalation_step_fails_and_leaves_the_step_unchanged()
    {
        var definition = Definition(escalationStepId: null);
        var instance = Instance(definition);

        var result = Fire(definition, instance, Timeout);

        Assert.False(result.Success);
        Assert.Contains("no EscalationStepId", result.Message);
        Assert.Equal("Start", instance.CurrentStepId);
    }

    [Fact]
    public void Explicit_NextSteps_edge_for_the_timeout_event_takes_precedence()
    {
        var definition = Definition("ManagerEscalation", explicitTimeoutEdge: true);
        var instance = Instance(definition);

        var result = Fire(definition, instance, Timeout);

        Assert.True(result.Success, result.Message);
        Assert.Equal("ExplicitTimeoutStep", instance.CurrentStepId);
    }

    [Fact]
    public void Ordinary_event_is_unaffected_by_the_sla()
    {
        var definition = Definition("ManagerEscalation");
        var instance = Instance(definition);

        var result = Fire(definition, instance, "Approved");

        Assert.True(result.Success, result.Message);
        Assert.Equal(WorkflowInstanceStatus.Completed, instance.Status);
    }

    private sealed class TestDomainEvent : FlowOS.Events.Models.DomainEvent
    {
        public TestDomainEvent(Guid tenantId, string eventType) : base(tenantId, eventType)
        {
        }
    }
}
