using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Entities.Orchestration;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

/// <summary>
/// ADR-006 Phase 4A: AgentWorkflowClass repository.
/// Table: "AgentWorkflowClasses". Never touches execution tables (Phase 4B).
/// </summary>
public sealed class AgentWorkflowClassRepository : IAgentWorkflowClassRepository
{
    private readonly FlowOSDbContext _context;

    public AgentWorkflowClassRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(AgentWorkflowClass plan, CancellationToken ct = default)
    {
        _context.AgentWorkflowClasses.Add(plan);
        await _context.SaveChangesAsync(ct);
    }

    public Task<AgentWorkflowClass?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _context.AgentWorkflowClasses
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == id, ct);

    public async Task<IReadOnlyList<AgentWorkflowClass>> ListAsync(
        Guid tenantId,
        WorkflowClassStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var query = _context.AgentWorkflowClasses
            .Where(p => p.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AgentWorkflowClass>> GetLineageAsync(
        Guid tenantId,
        Guid id,
        int maxDepth = 10,
        CancellationToken ct = default)
    {
        var chain = new List<AgentWorkflowClass>();
        var currentId = (Guid?)id;

        while (currentId.HasValue && chain.Count < maxDepth)
        {
            var item = await _context.AgentWorkflowClasses
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == currentId.Value, ct);

            if (item is null) break;

            chain.Add(item);
            currentId = item.PreviousVersionId;
        }

        return chain;
    }

    public Task<bool> HasExecutionsAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        // Phase 4B will add AgentWorkflowExecutions. Until then, no executions exist.
        Task.FromResult(false);

    public async Task UpdateAsync(AgentWorkflowClass plan, CancellationToken ct = default)
    {
        _context.AgentWorkflowClasses.Update(plan);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var plan = await _context.AgentWorkflowClasses
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == id, ct);

        if (plan is null) return;

        _context.AgentWorkflowClasses.Remove(plan);
        await _context.SaveChangesAsync(ct);
    }
}
