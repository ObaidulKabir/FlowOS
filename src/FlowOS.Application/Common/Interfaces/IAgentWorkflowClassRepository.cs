using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Domain.Entities.Orchestration;
using FlowOS.Domain.Enums;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>
/// Repository for <see cref="AgentWorkflowClass"/> declarative plan templates.
///
/// ADR-006: AgentWorkflowClass is the blueprint-only entity (Phase 4A).
/// Execution records (AgentWorkflowExecution) arrive in Phase 4B.
/// </summary>
public interface IAgentWorkflowClassRepository
{
    /// <summary>Persists a new plan template. Id must be unique.</summary>
    Task AddAsync(AgentWorkflowClass plan, CancellationToken ct = default);

    /// <summary>Retrieves a plan by primary key, scoped to the tenant.</summary>
    Task<AgentWorkflowClass?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lists plans for the tenant, optionally filtered by status.
    /// Returns most recently created first.
    /// </summary>
    Task<IReadOnlyList<AgentWorkflowClass>> ListAsync(
        Guid tenantId,
        WorkflowClassStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the lineage chain starting from <paramref name="id"/> and walking
    /// PreviousVersionId links, newest first.  Stops at <paramref name="maxDepth"/>.
    /// </summary>
    Task<IReadOnlyList<AgentWorkflowClass>> GetLineageAsync(
        Guid tenantId, Guid id, int maxDepth = 10, CancellationToken ct = default);

    /// <summary>Returns true when the tenant has at least one execution against this plan.</summary>
    Task<bool> HasExecutionsAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Persists changes to an existing plan (lifecycle transitions, deprecation).</summary>
    Task UpdateAsync(AgentWorkflowClass plan, CancellationToken ct = default);

    /// <summary>Hard-deletes a Draft plan with no executions.</summary>
    Task DeleteAsync(Guid tenantId, Guid id, CancellationToken ct = default);
}
