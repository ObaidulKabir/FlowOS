using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowOS.Domain.Entities.Orchestration;

/// <summary>
/// The runtime instance of an <see cref="AgentTeam"/> template.
///
/// ADR-001 / ADR-002:
///   Mirrors the WorkflowClass → WorkflowInstance pattern.
///   An AgentTeam is the immutable definition; a TeamExecution is the live, stateful execution
///   tracking which agent was assigned to which slot, what tasks were completed, and the
///   overall execution outcome.
///
///   Table: TeamExecutions — NEVER "Teams" (ADR-002).
/// </summary>
public sealed class TeamExecution
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>The AgentTeam template this execution was instantiated from.</summary>
    public Guid AgentTeamId { get; private set; }

    /// <summary>
    /// The workflow instance that triggered this team execution, if any.
    /// Null for executions spawned directly by the Objective Orchestrator.
    /// </summary>
    public Guid? WorkflowInstanceId { get; private set; }

    /// <summary>The step within the workflow instance that hosts this execution.</summary>
    public string? WorkflowStepId { get; private set; }

    /// <summary>Human-readable objective for this execution run.</summary>
    public string? Objective { get; private set; }

    public TeamExecutionStatus Status { get; private set; }

    /// <summary>
    /// Slot assignments: RoleName → AgentId.
    /// Populated as the orchestrator assigns agents to slots at runtime.
    /// Stored as JSON (see TeamExecutionConfiguration).
    /// </summary>
    public IReadOnlyDictionary<string, string> SlotAssignments { get; private set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>Sanitised failure message when Status = Failed.</summary>
    public string? FailureReason { get; private set; }

    // ── ADR-005 Phase 3: Outcome fields ─────────────────────────────────────
    // These are written once by Complete() and are the primary signal the
    // External Orchestrator Agent reads via get_team_execution_outcome to make
    // its next sequencing decision.

    /// <summary>
    /// ADR-005 Phase 3: The workflow event the agent committed (e.g. "RISK_LOW",
    /// "APPROVED", "REJECTED"). Null until the execution completes successfully.
    /// </summary>
    public string? CommittedEvent { get; private set; }

    /// <summary>
    /// ADR-005 Phase 3: Agent confidence score at commit time, in the range
    /// [0.0, 1.0]. Null when not provided or when the execution did not complete.
    /// </summary>
    public double? Confidence { get; private set; }

    /// <summary>
    /// ADR-005 Phase 3: The specific agent that acted on the slot (e.g.
    /// "Agent:risk-analyst-v2"). Null until the execution completes.
    /// Never contains raw tenant IDs — use FlowOsTelemetry.TenantHash in logs.
    /// </summary>
    public string? ActorAgentId { get; private set; }

    private TeamExecution() { }

    /// <summary>
    /// Factory: creates a new TeamExecution in Pending status.
    /// </summary>
    public static TeamExecution Create(
        Guid tenantId,
        Guid agentTeamId,
        Guid? workflowInstanceId,
        string? workflowStepId,
        string? objective,
        DateTime createdAtUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (agentTeamId == Guid.Empty)
            throw new ArgumentException("AgentTeamId is required.", nameof(agentTeamId));

        return new TeamExecution
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentTeamId = agentTeamId,
            WorkflowInstanceId = workflowInstanceId,
            WorkflowStepId = string.IsNullOrWhiteSpace(workflowStepId) ? null : workflowStepId.Trim(),
            Objective = string.IsNullOrWhiteSpace(objective) ? null : objective.Trim()[..Math.Min(objective.Trim().Length, 2000)],
            Status = TeamExecutionStatus.Pending,
            SlotAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CreatedAtUtc = EnsureUtc(createdAtUtc)
        };
    }

    /// <summary>
    /// Assigns an agent to a named slot. Idempotent: reassigning the same role overwrites.
    /// </summary>
    public void AssignAgent(string roleName, string agentId, DateTime assignedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("RoleName is required.", nameof(roleName));
        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        if (Status == TeamExecutionStatus.Completed || Status == TeamExecutionStatus.Failed)
            throw new InvalidOperationException($"Cannot assign agent to a terminal execution (status: {Status}).");

        var assignments = new Dictionary<string, string>(
            SlotAssignments,
            StringComparer.OrdinalIgnoreCase)
        {
            [roleName.Trim()] = agentId.Trim()
        };
        SlotAssignments = assignments;

        if (Status == TeamExecutionStatus.Pending)
        {
            Status = TeamExecutionStatus.Active;
            StartedAtUtc = EnsureUtc(assignedAtUtc);
        }
    }

    /// <summary>
    /// Marks the execution as successfully completed.
    /// ADR-005 Phase 3: <paramref name="committedEvent"/>, <paramref name="confidence"/>,
    /// and <paramref name="actorAgentId"/> are written once and become the primary
    /// signal for the External Orchestrator Agent's next sequencing decision.
    /// </summary>
    /// <param name="completedAtUtc">Completion timestamp (must be UTC).</param>
    /// <param name="committedEvent">
    ///   Optional. The workflow event the agent committed (e.g. "RISK_LOW").
    ///   If provided, must not exceed 200 characters.
    /// </param>
    /// <param name="confidence">
    ///   Optional. Agent confidence score in [0.0, 1.0].
    ///   Values outside the range are clamped.
    /// </param>
    /// <param name="actorAgentId">
    ///   Optional. The agent identifier that acted on the step
    ///   (e.g. "Agent:risk-analyst-v2"). Must not exceed 200 characters.
    /// </param>
    public void Complete(
        DateTime completedAtUtc,
        string? committedEvent = null,
        double? confidence = null,
        string? actorAgentId = null)
    {
        if (Status == TeamExecutionStatus.Failed)
            throw new InvalidOperationException("A failed execution cannot be completed.");

        Status = TeamExecutionStatus.Completed;
        CompletedAtUtc = EnsureUtc(completedAtUtc);

        if (!string.IsNullOrWhiteSpace(committedEvent))
        {
            var trimmed = committedEvent.Trim();
            CommittedEvent = trimmed.Length > 200 ? trimmed[..200] : trimmed;
        }

        if (confidence.HasValue)
            Confidence = Math.Clamp(confidence.Value, 0.0, 1.0);

        if (!string.IsNullOrWhiteSpace(actorAgentId))
        {
            var trimmed = actorAgentId.Trim();
            ActorAgentId = trimmed.Length > 200 ? trimmed[..200] : trimmed;
        }
    }

    /// <summary>Marks the execution as failed with a sanitised reason.</summary>
    public void Fail(string sanitizedReason, DateTime failedAtUtc)
    {
        if (Status == TeamExecutionStatus.Completed)
            throw new InvalidOperationException("A completed execution cannot be failed.");

        Status = TeamExecutionStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(sanitizedReason)
            ? "Unknown failure."
            : sanitizedReason.Trim()[..Math.Min(sanitizedReason.Trim().Length, 2000)];
        CompletedAtUtc = EnsureUtc(failedAtUtc);
    }

    /// <summary>Returns the agent assigned to the given slot role, or null if not yet assigned.</summary>
    public string? GetAssignedAgent(string roleName) =>
        SlotAssignments.TryGetValue(roleName, out var agentId) ? agentId : null;

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}

/// <summary>ADR-002: Lifecycle of a TeamExecution (never conflated with WorkflowInstanceStatus).</summary>
public enum TeamExecutionStatus
{
    /// <summary>Created but no agent has been assigned yet.</summary>
    Pending = 0,
    /// <summary>At least one agent has been assigned and execution has started.</summary>
    Active = 1,
    /// <summary>All required slots completed their tasks successfully.</summary>
    Completed = 2,
    /// <summary>Execution terminated due to an unrecoverable error.</summary>
    Failed = 3
}
