using System;
using System.Collections.Generic;

namespace FlowOS.Events.Models;

/// <summary>
/// ADR-005 Phase 3: Emitted when an AgentTask workflow step completes successfully.
///
/// This is the primary signal the External Orchestrator Agent reads (via
/// read_change_feed or get_team_execution_outcome) to make its next cross-team
/// sequencing decision in a StrategicPlan WorkflowClass.
///
/// Three inviolable rules (ADR-005):
///   1. FlowOS never sequences across teams — that is the orchestrator's job.
///   2. The orchestrator reads state from FlowOS; it never maintains its own copy.
///   3. The StrategicPlan WorkflowClass is the source of truth.
/// </summary>
public sealed class AgentTaskCompleted : DomainEvent
{
    // ── Step / plan context ──────────────────────────────────────────────────

    /// <summary>The workflow instance (StrategicPlan or any AgentTask workflow).</summary>
    public Guid WorkflowInstanceId { get; private set; }

    /// <summary>The step within the workflow that was completed.</summary>
    public string StepId { get; private set; } = string.Empty;

    // ── TeamExecution outcome ────────────────────────────────────────────────

    /// <summary>The TeamExecution record that completed the step.</summary>
    public Guid TeamExecutionId { get; private set; }

    /// <summary>
    /// The workflow event the agent committed (e.g. "RISK_LOW", "APPROVED").
    /// Null when the step completed without a committed event.
    /// </summary>
    public string? CommittedEvent { get; private set; }

    /// <summary>Agent confidence score at commit time, in [0.0, 1.0]. Null when not provided.</summary>
    public double? Confidence { get; private set; }

    /// <summary>
    /// The specific agent that acted (e.g. "Agent:risk-analyst-v2").
    /// Never contains raw tenant IDs — see FlowOsTelemetry.TenantHash.
    /// </summary>
    public string? ActorAgentId { get; private set; }

    /// <summary>RoleName → AgentId assignments for this execution.</summary>
    public IReadOnlyDictionary<string, string> SlotAssignments { get; private set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>UTC timestamp when the TeamExecution completed.</summary>
    public DateTime CompletedAtUtc { get; private set; }

    public AgentTaskCompleted(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        Guid teamExecutionId,
        string? committedEvent,
        double? confidence,
        string? actorAgentId,
        IReadOnlyDictionary<string, string> slotAssignments,
        DateTime completedAtUtc)
        : base(tenantId, "AgentTaskCompleted")
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (workflowInstanceId == Guid.Empty)
            throw new ArgumentException("WorkflowInstanceId is required.", nameof(workflowInstanceId));
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("StepId is required.", nameof(stepId));
        if (teamExecutionId == Guid.Empty)
            throw new ArgumentException("TeamExecutionId is required.", nameof(teamExecutionId));

        WorkflowInstanceId = workflowInstanceId;
        StepId = stepId.Trim();
        TeamExecutionId = teamExecutionId;
        CommittedEvent = Truncate(committedEvent, 200);
        Confidence = confidence.HasValue ? Math.Clamp(confidence.Value, 0.0, 1.0) : null;
        ActorAgentId = Truncate(actorAgentId, 200);
        SlotAssignments = slotAssignments
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CompletedAtUtc = completedAtUtc.Kind == DateTimeKind.Utc
            ? completedAtUtc
            : DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc);

        // Populate the base Metadata dictionary so ChangeFeedReader surfaces these
        // fields in the standard change-feed item shape.
        AddMetadata("WorkflowInstanceId", workflowInstanceId.ToString());
        AddMetadata("StepId", StepId);
        AddMetadata("TeamExecutionId", teamExecutionId.ToString());
        if (CommittedEvent != null) AddMetadata("CommittedEvent", CommittedEvent);
        if (Confidence.HasValue)    AddMetadata("Confidence", Confidence.Value.ToString("0.####"));
        if (ActorAgentId != null)   AddMetadata("ActorAgentId", ActorAgentId);
        AddMetadata("CompletedAtUtc", CompletedAtUtc.ToString("O"));
    }

    // EF Core requires a private parameterless constructor.
    private AgentTaskCompleted() { }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
