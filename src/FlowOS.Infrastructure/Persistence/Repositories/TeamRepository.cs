using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class TeamRepository : ITeamRepository
{
    private readonly FlowOSDbContext _context;

    public TeamRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<Team?> GetByIdAsync(Guid tenantId, Guid teamId, CancellationToken cancellationToken = default)
        => _context.Set<Team>()
            .Include(t => t.Hierarchy)
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == teamId, cancellationToken);

    public Task<List<Team>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _context.Set<Team>()
            .Include(t => t.Hierarchy)
            .Include(t => t.Members)
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

    public async Task<List<Team>> ListByCapabilitiesAsync(
        Guid tenantId,
        IReadOnlyCollection<string> requiredCapabilities,
        CancellationToken cancellationToken = default)
    {
        if (requiredCapabilities == null || requiredCapabilities.Count == 0)
            return await ListByTenantAsync(tenantId, cancellationToken);

        // Fetch teams for the tenant
        var teams = await _context.Set<Team>()
            .Include(t => t.Hierarchy)
            .Include(t => t.Members)
            .Where(t => t.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        // Client-side filtering because Capabilities is a JSON array
        return teams
            .Where(t => requiredCapabilities.All(req => 
                t.Capabilities.Any(cap => string.Equals(cap, req, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    public async Task<bool> IsUserAuthorizedForCapabilitiesAsync(
        Guid tenantId,
        Guid tenantUserId,
        IReadOnlyCollection<string> requiredCapabilities,
        int minHierarchyOrder = 0,
        CancellationToken cancellationToken = default)
    {
        // 1. Get all teams where the user is a member at >= minHierarchyOrder
        var userTeams = await _context.Set<Team>()
            .Where(t => t.TenantId == tenantId && 
                        t.Members.Any(m => m.TenantUserId == tenantUserId && m.HierarchyOrder >= minHierarchyOrder))
            .ToListAsync(cancellationToken);

        if (userTeams.Count == 0)
            return false;

        if (requiredCapabilities == null || requiredCapabilities.Count == 0)
            return true;

        // 2. Check if ANY of those teams possess ALL the required capabilities
        return userTeams.Any(t => 
            requiredCapabilities.All(req => 
                t.Capabilities.Any(cap => string.Equals(cap, req, StringComparison.OrdinalIgnoreCase))));
    }

    public void Add(Team team) => _context.Set<Team>().Add(team);

    public void Remove(Team team) => _context.Set<Team>().Remove(team);
}
