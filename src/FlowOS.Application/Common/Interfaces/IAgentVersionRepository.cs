using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Domain.Entities.Orchestration;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>
/// Repository for immutable AgentVersion snapshots.
/// Tenant isolation is enforced on every method — callers must always supply tenantId.
/// </summary>
public interface IAgentVersionRepository
{
    /// <summary>Persists a newly published version. Id must be unique.</summary>
    Task AddAsync(AgentVersion version, CancellationToken ct = default);

    /// <summary>Retrieves a specific version by primary key, scoped to the tenant.</summary>
    Task<AgentVersion?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Retrieves a specific version by (agentId, version number), scoped to the tenant.</summary>
    Task<AgentVersion?> GetByAgentVersionAsync(
        Guid tenantId, string agentId, int version, CancellationToken ct = default);

    /// <summary>
    /// Returns the highest published version number for the given agentId within the tenant.
    /// Returns 0 if no versions exist yet.
    /// </summary>
    Task<int> GetLatestVersionNumberAsync(
        Guid tenantId, string agentId, CancellationToken ct = default);

    /// <summary>
    /// Returns all versions for an agent, ordered by Version ascending, scoped to tenant.
    /// </summary>
    Task<IReadOnlyList<AgentVersion>> ListByAgentIdAsync(
        Guid tenantId, string agentId, CancellationToken ct = default);

    /// <summary>
    /// Returns active (non-deprecated) versions whose CapabilityTags satisfy all required tags.
    /// Used by the orchestrator for capability-based agent selection.
    /// </summary>
    Task<IReadOnlyList<AgentVersion>> FindByCapabilitiesAsync(
        Guid tenantId, IReadOnlyList<string> requiredTags, CancellationToken ct = default);

    /// <summary>
    /// Persists deprecation state. Called after AgentVersion.Deprecate() has been invoked.
    /// </summary>
    Task SaveDeprecationAsync(AgentVersion version, CancellationToken ct = default);
}
