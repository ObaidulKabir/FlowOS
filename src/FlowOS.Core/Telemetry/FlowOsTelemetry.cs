using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;

namespace FlowOS.Core.Telemetry;

/// <summary>
/// Trace sources, metrics, and attribute names shared by every FlowOS component (CR-07).
/// Uses only System.Diagnostics, so domain and engine projects can emit telemetry without
/// depending on the OpenTelemetry SDK; the hosts register the SDK and exporters.
/// Telemetry never carries personal data: tenants appear only as a one-way hash.
/// </summary>
public static class FlowOsTelemetry
{
    public const string WorkflowSourceName = "FlowOS.Workflows.Engine";
    public const string BackgroundSourceName = "FlowOS.Infrastructure.Background";
    public const string MeterName = "FlowOS";

    /// <summary>Activity source for workflow engine spans (<c>workflow.advance</c>).</summary>
    public static readonly ActivitySource Workflows = new(WorkflowSourceName);

    /// <summary>Activity source for background processing spans (<c>outbox.process</c>, <c>workflow.timer.process</c>).</summary>
    public static readonly ActivitySource Background = new(BackgroundSourceName);

    public static readonly Meter Meter = new(MeterName);

    /// <summary>Duration of one workflow advance (transition attempt), in milliseconds.</summary>
    public static readonly Histogram<double> TransitionDuration = Meter.CreateHistogram<double>(
        "flowos.workflow.transition.duration", unit: "ms", description: "Duration of a workflow advance call.");

    /// <summary>Workflow advance calls, tagged with <c>outcome</c> = succeeded | failed.</summary>
    public static readonly Counter<long> Transitions = Meter.CreateCounter<long>(
        "flowos.workflow.transitions", unit: "{transition}", description: "Workflow advance calls by outcome.");

    /// <summary>Outbox messages handled, tagged with <c>outcome</c> = processed | failed | dead_letter.</summary>
    public static readonly Counter<long> OutboxMessages = Meter.CreateCounter<long>(
        "flowos.outbox.messages", unit: "{message}", description: "Outbox messages handled by outcome.");

    /// <summary>Time from an outbox message being written to being picked up, in milliseconds.</summary>
    public static readonly Histogram<double> OutboxLag = Meter.CreateHistogram<double>(
        "flowos.outbox.lag", unit: "ms", description: "Delay between writing and processing an outbox message.");

    /// <summary>Time from a timer's due time to its execution, in milliseconds.</summary>
    public static readonly Histogram<double> TimerLag = Meter.CreateHistogram<double>(
        "flowos.timer.lag", unit: "ms", description: "Delay between a timer's due time and its execution.");

    // Attribute names
    public const string TenantHashAttribute = "flowos.tenant.hash";
    public const string CorrelationIdAttribute = "flowos.correlation_id";
    public const string CausationIdAttribute = "flowos.causation_id";
    public const string OutcomeAttribute = "outcome";

    /// <summary>
    /// One-way, stable pseudonym for a tenant (first 16 hex characters of SHA-256 of the tenant ID).
    /// Lets operators group telemetry by tenant without exporting the tenant identifier itself.
    /// </summary>
    public static string TenantHash(Guid tenantId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(tenantId.ToByteArray(), hash);
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    /// <summary>
    /// W3C <c>traceparent</c> of the current activity, or null when there is none or it does not use
    /// the W3C format. Stored with outbox messages so the consumer continues the producer's trace.
    /// </summary>
    public static string? CurrentTraceParent()
    {
        var current = Activity.Current;
        return current is { IdFormat: ActivityIdFormat.W3C } ? current.Id : null;
    }

    /// <summary>Parses a stored <c>traceparent</c>; returns default when absent or invalid.</summary>
    public static ActivityContext ParentFrom(string? traceParent) =>
        !string.IsNullOrWhiteSpace(traceParent) && ActivityContext.TryParse(traceParent, null, out var context)
            ? context
            : default;
}
