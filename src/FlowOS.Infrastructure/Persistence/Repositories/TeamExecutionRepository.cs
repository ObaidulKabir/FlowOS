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
/// ADR-002: TeamExecution repository is entirely separate from IAM TeamRepository.
/// Table: "TeamExecutions". Never touches the "Teams" table.
/// </summary>
public sealed class TeamExecutionRepository : ITeamExecutionRepository
{
    private readonly FlowOSDbContext _context;

    public TeamExecutionRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(TeamExecution execution, CancellationToken ct = default)
    {
        _context.TeamExecutions.Add(execution);
        await _context.SaveChangesAsync(ct);
    }

    public Task<TeamExecution?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _context.TeamExecutions
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);

    public Task<TeamExecution?> GetActiveForStepAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        CancellationToken ct = default) =>
        _context.TeamExecutions
            .FirstOrDefaultAsync(
                e => e.TenantId == tenantId
                     && e.WorkflowInstanceId == workflowInstanceId
                     && e.WorkflowStepId == stepId
                     && (e.Status == TeamExecutionStatus.Pending || e.Status == TeamExecutionStatus.Active),
                ct);

    public async Task<IReadOnlyList<TeamExecution>> ListByTeamAsync(
        Guid tenantId,
        Guid agentTeamId,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        return await _context.TeamExecutions
            .Where(e => e.TenantId == tenantId && e.AgentTeamId == agentTeamId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(TeamExecution execution, CancellationToken ct = default)
    {
        _context.TeamExecutions.Update(execution);
        await _context.SaveChangesAsync(ct);
    }
}
