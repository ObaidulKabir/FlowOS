using System.Collections.Concurrent;
using System.Diagnostics;
using FlowOS.Core.Common.Models;
using FlowOS.Core.Telemetry;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Xunit;

namespace FlowOS.UnitTests.Telemetry;

/// <summary>CR-07: spans are emitted, carry no raw tenant IDs, and outbox messages keep the producer trace.</summary>
public class FlowOsTelemetryTests
{
    [Fact]
    public void TenantHash_is_stable_short_and_does_not_contain_the_tenant_id()
    {
        var tenantId = Guid.NewGuid();

        var first = FlowOsTelemetry.TenantHash(tenantId);
        var second = FlowOsTelemetry.TenantHash(tenantId);

        Assert.Equal(first, second);
        Assert.Equal(16, first.Length);
        Assert.Matches("^[0-9a-f]{16}$", first);
        Assert.DoesNotContain(tenantId.ToString("N")[..8], first);
        Assert.NotEqual(first, FlowOsTelemetry.TenantHash(Guid.NewGuid()));
    }

    [Fact]
    public void OutboxMessage_captures_the_current_traceparent_and_the_consumer_can_continue_it()
    {
        using var source = new ActivitySource("FlowOS.UnitTests.Telemetry.Producer");
        using var listener = ListenTo(source.Name, new ConcurrentBag<Activity>());

        OutboxMessage message;
        string expectedTraceId;
        using (var producer = source.StartActivity("api.request"))
        {
            Assert.NotNull(producer);
            expectedTraceId = producer!.TraceId.ToString();
            message = new OutboxMessage(Guid.NewGuid(), "TestEvent", "{}");
        }

        Assert.NotNull(message.TraceParent);
        var parent = FlowOsTelemetry.ParentFrom(message.TraceParent);
        Assert.Equal(expectedTraceId, parent.TraceId.ToString());
    }

    [Fact]
    public void OutboxMessage_without_an_active_trace_has_no_traceparent()
    {
        Activity.Current = null;
        var message = new OutboxMessage(Guid.NewGuid(), "TestEvent", "{}");

        Assert.Null(message.TraceParent);
        Assert.Equal(default, FlowOsTelemetry.ParentFrom(message.TraceParent));
    }

    [Fact]
    public void ParentFrom_ignores_invalid_values()
    {
        Assert.Equal(default, FlowOsTelemetry.ParentFrom("not-a-traceparent"));
        Assert.Equal(default, FlowOsTelemetry.ParentFrom(null));
    }

    [Fact]
    public void Workflow_advance_span_uses_tenant_hash_and_records_outcome()
    {
        var tenantId = Guid.NewGuid();
        var captured = new ConcurrentBag<Activity>();
        using var listener = ListenTo(FlowOsTelemetry.WorkflowSourceName, captured);

        var definition = new WorkflowDefinition(tenantId, "TelemetryCheck");
        var start = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
        start.NextSteps.Add("Go", "END");
        definition.AddStep(start);
        definition.Publish();
        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.Empty, definition.Version, definition.StartStepId);

        var result = new WorkflowEngine(new StateMachineEngine())
            .Advance(instance, definition, new TestDomainEvent(tenantId, "Go"), new FlowOS.StateMachines.Models.ExecutionContext());

        Assert.True(result.Success);
        var span = Assert.Single(captured, a => Equals(a.GetTagItem("workflow.instanceId"), instance.Id));
        Assert.Equal("workflow.advance", span.OperationName);
        Assert.Equal(FlowOsTelemetry.TenantHash(tenantId), span.GetTagItem(FlowOsTelemetry.TenantHashAttribute));
        Assert.Equal("succeeded", span.GetTagItem(FlowOsTelemetry.OutcomeAttribute));
        Assert.DoesNotContain(span.TagObjects, tag => Equals(tag.Value, tenantId) || Equals(tag.Value, tenantId.ToString()));
    }

    private sealed class TestDomainEvent : FlowOS.Events.Models.DomainEvent
    {
        public TestDomainEvent(Guid tenantId, string eventType) : base(tenantId, eventType)
        {
        }
    }

    private static ActivityListener ListenTo(string sourceName, ConcurrentBag<Activity> captured)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = captured.Add
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
