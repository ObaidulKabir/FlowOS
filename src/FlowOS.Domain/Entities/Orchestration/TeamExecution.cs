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

    /// <summary>Marks the execution as successfully completed.</summary>
    public void Complete(DateTime completedAtUtc)
    {
        if (Status == TeamExecutionStatus.Failed)
            throw new InvalidOperationException("A failed execution cannot be completed.");

        Status = TeamExecutionStatus.Completed;
        CompletedAtUtc = EnsureUtc(completedAtUtc);
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
