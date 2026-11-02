using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Entities.Orchestration;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

/// <summary>
/// ADR-002: AgentTeam repository is entirely separate from IAM TeamRepository.
/// Table: "AgentTeams". Never touches the "Teams" table.
/// </summary>
public sealed class AgentTeamRepository : IAgentTeamRepository
{
    private readonly FlowOSDbContext _context;

    public AgentTeamRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(AgentTeam team, CancellationToken ct = default)
    {
        _context.AgentTeams.Add(team);
        await _context.SaveChangesAsync(ct);
    }

    public Task<AgentTeam?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _context.AgentTeams
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public Task<AgentTeam?> GetByNameAsync(Guid tenantId, string name, CancellationToken ct = default) =>
        _context.AgentTeams
            .FirstOrDefaultAsync(
                t => t.TenantId == tenantId
                     && t.Name == name, ct);

    public async Task<IReadOnlyList<AgentTeam>> ListAsync(
        Guid tenantId,
        AgentTeamStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var query = _context.AgentTeams
            .Where(t => t.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        return await query
            .OrderByDescending(t => t.UpdatedAtUtc ?? t.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AgentTeam>> FindActiveByCapabilitiesAsync(
        Guid tenantId, IReadOnlyList<string> requiredTags, CancellationToken ct = default)
    {
        var candidates = await _context.AgentTeams
            .Where(t => t.TenantId == tenantId && t.Status == AgentTeamStatus.Active)
            .ToListAsync(ct);

        if (requiredTags == null || requiredTags.Count == 0)
            return candidates;

        return candidates
            .Where(t => requiredTags.All(req =>
                t.CapabilityTags.Any(cap =>
                    string.Equals(cap, req, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(t => t.GetCapabilityMatchScore(requiredTags))
            .ToList();
    }

    public async Task UpdateAsync(AgentTeam team, CancellationToken ct = default)
    {
        _context.AgentTeams.Update(team);
        await _context.SaveChangesAsync(ct);
    }
}
