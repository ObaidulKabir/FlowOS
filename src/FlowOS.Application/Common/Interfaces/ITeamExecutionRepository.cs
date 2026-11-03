using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Domain.Entities.Orchestration;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>
/// Repository for <see cref="TeamExecution"/> runtime instances.
///
/// ADR-002: TeamExecution tracks runtime state for an AgentTeam template execution.
/// Table: TeamExecutions — never "Teams".
/// </summary>
public interface ITeamExecutionRepository
{
    /// <summary>Persists a new team execution record.</summary>
    Task AddAsync(TeamExecution execution, CancellationToken ct = default);

    /// <summary>Retrieves a team execution by primary key, scoped to the tenant.</summary>
    Task<TeamExecution?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the active team execution for a specific workflow step, if one exists.
    /// Used to prevent duplicate executions for the same step.
    /// </summary>
    Task<TeamExecution?> GetActiveForStepAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists executions for a given AgentTeam template within the tenant.
    /// Returns most recently created first.
    /// </summary>
    Task<IReadOnlyList<TeamExecution>> ListByTeamAsync(
        Guid tenantId,
        Guid agentTeamId,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);

    /// <summary>Persists status or slot-assignment changes to an existing execution.</summary>
    Task UpdateAsync(TeamExecution execution, CancellationToken ct = default);
}
