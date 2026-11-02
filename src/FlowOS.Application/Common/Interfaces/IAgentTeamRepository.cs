using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Domain.Entities.Orchestration;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>
/// Repository for AgentTeam orchestration templates.
///
/// ADR-002: This repository is SEPARATE from any IAM Team repository.
/// AgentTeam (multi-agent orchestration) must never share a repository with Team (human IAM).
/// </summary>
public interface IAgentTeamRepository
{
    /// <summary>Persists a new team template. Id must be unique.</summary>
    Task AddAsync(AgentTeam team, CancellationToken ct = default);

    /// <summary>Retrieves a team by primary key, scoped to the tenant.</summary>
    Task<AgentTeam?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Retrieves a team by name within the tenant (names are unique per tenant).</summary>
    Task<AgentTeam?> GetByNameAsync(Guid tenantId, string name, CancellationToken ct = default);

    /// <summary>
    /// Lists all teams for the tenant, optionally filtered by status.
    /// Returns most recently updated first.
    /// </summary>
    Task<IReadOnlyList<AgentTeam>> ListAsync(
        Guid tenantId,
        AgentTeamStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Lists active teams whose CapabilityTags satisfy all required tags.
    /// Used by the Objective Orchestrator for team selection.
    /// </summary>
    Task<IReadOnlyList<AgentTeam>> FindActiveByCapabilitiesAsync(
        Guid tenantId, IReadOnlyList<string> requiredTags, CancellationToken ct = default);

    /// <summary>
    /// Persists changes to an existing team (profile updates, slot changes, status transitions).
    /// Optimistic concurrency is enforced at the infrastructure level.
    /// </summary>
    Task UpdateAsync(AgentTeam team, CancellationToken ct = default);
}
