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

public sealed class AgentVersionRepository : IAgentVersionRepository
{
    private readonly FlowOSDbContext _context;

    public AgentVersionRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(AgentVersion version, CancellationToken ct = default)
    {
        _context.AgentVersions.Add(version);
        await _context.SaveChangesAsync(ct);
    }

    public Task<AgentVersion?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _context.AgentVersions
            .FirstOrDefaultAsync(v => v.TenantId == tenantId && v.Id == id, ct);

    public Task<AgentVersion?> GetByAgentVersionAsync(
        Guid tenantId, string agentId, int version, CancellationToken ct = default) =>
        _context.AgentVersions
            .FirstOrDefaultAsync(
                v => v.TenantId == tenantId
                     && v.AgentId == agentId
                     && v.Version == version, ct);

    public async Task<int> GetLatestVersionNumberAsync(
        Guid tenantId, string agentId, CancellationToken ct = default)
    {
        var max = await _context.AgentVersions
            .Where(v => v.TenantId == tenantId && v.AgentId == agentId)
            .MaxAsync(v => (int?)v.Version, ct);
        return max ?? 0;
    }

    public Task<IReadOnlyList<AgentVersion>> ListByAgentIdAsync(
        Guid tenantId, string agentId, CancellationToken ct = default) =>
        _context.AgentVersions
            .Where(v => v.TenantId == tenantId && v.AgentId == agentId)
            .OrderBy(v => v.Version)
            .ToListAsync(ct)
            .ContinueWith<IReadOnlyList<AgentVersion>>(t => t.Result, ct);

    public async Task<IReadOnlyList<AgentVersion>> FindByCapabilitiesAsync(
        Guid tenantId, IReadOnlyList<string> requiredTags, CancellationToken ct = default)
    {
        var candidates = await _context.AgentVersions
            .Where(v => v.TenantId == tenantId && !v.IsDeprecated)
            .ToListAsync(ct);

        // Post-filter in memory: JSONB contains checks are supported but the capability
        // set is typically small enough that in-memory filtering is safe here.
        return candidates
            .Where(v => v.SatisfiesCapabilities(requiredTags))
            .ToList();
    }

    public async Task SaveDeprecationAsync(AgentVersion version, CancellationToken ct = default)
    {
        _context.AgentVersions.Update(version);
        await _context.SaveChangesAsync(ct);
    }
}
