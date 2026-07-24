using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class WorkflowInstanceRepository : IWorkflowInstanceRepository
{
    private readonly FlowOSDbContext _context;

    public WorkflowInstanceRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<WorkflowInstance?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.WorkflowInstances
            .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId, cancellationToken);

    public Task<WorkflowInstance?> GetByIdAsNoTrackingAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId, cancellationToken);

    public Task<WorkflowInstance?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<List<WorkflowInstance>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _context.WorkflowInstances
            .AsNoTracking()
            .Where(w => w.TenantId == tenantId)
            .OrderByDescending(w => w.Id)
            .ToListAsync(cancellationToken);

    public Task<List<WorkflowInstance>> ListByStatusAsync(WorkflowInstanceStatus status, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var query = _context.WorkflowInstances
            .AsNoTracking()
            .Where(w => w.Status == status);

        if (tenantId.HasValue)
            query = query.Where(w => w.TenantId == tenantId.Value);

        return query.ToListAsync(cancellationToken);
    }

    public async Task<List<WorkflowSummaryDto>> GetSummariesByTenantAsync(Guid tenantId, WorkflowInstanceStatus? status, CancellationToken cancellationToken = default)
    {
        var query = from w in _context.WorkflowInstances.AsNoTracking()
                    join wc in _context.WorkflowClasses.AsNoTracking() on w.WorkflowClassId equals wc.Id into wcGroup
                    from wc in wcGroup.DefaultIfEmpty()
                    where w.TenantId == tenantId
                    select new { w, Name = wc != null ? wc.Name : "Unknown" };

        if (status.HasValue)
            query = query.Where(x => x.w.Status == status.Value);

        var instances = await query
            .OrderByDescending(x => x.w.CreatedAt)
            .ToListAsync(cancellationToken);

        return instances.Select(x => MapSummary(x.w, x.Name)).ToList();
    }

    public async Task<WorkflowSummaryDto?> GetSummaryByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var result = await (from w in _context.WorkflowInstances.AsNoTracking()
                            join wc in _context.WorkflowClasses.AsNoTracking() on w.WorkflowClassId equals wc.Id into wcGroup
                            from wc in wcGroup.DefaultIfEmpty()
                            where w.Id == id && w.TenantId == tenantId
                            select new { w, Name = wc != null ? wc.Name : "Unknown" })
            .FirstOrDefaultAsync(cancellationToken);

        return result == null ? null : MapSummary(result.w, result.Name);
    }

    public Task<bool> AnyForWorkflowClassAsync(Guid workflowClassId, CancellationToken cancellationToken = default)
        => _context.WorkflowInstances.AnyAsync(w => w.WorkflowClassId == workflowClassId, cancellationToken);

    public void Add(WorkflowInstance instance) => _context.WorkflowInstances.Add(instance);

    private static WorkflowSummaryDto MapSummary(WorkflowInstance w, string className) => new()
    {
        Id = w.Id,
        WorkflowId = w.Id,
        WorkflowClassId = w.WorkflowClassId,
        WorkflowClassName = className,
        DefinitionId = w.WorkflowDefinitionId,
        Version = w.WorkflowVersion,
        CurrentStepId = w.CurrentStepId,
        CurrentStep = w.CurrentStepId,
        Status = w.Status.ToString(),
        CorrelationId = w.CorrelationId,
        CreatedAt = w.CreatedAt,
        CompletedAt = w.CompletedAt
    };
}
