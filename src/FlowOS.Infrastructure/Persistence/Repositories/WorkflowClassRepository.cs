using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class WorkflowClassRepository : IWorkflowClassRepository
{
    private readonly FlowOSDbContext _context;

    public WorkflowClassRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<WorkflowClass?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.WorkflowClasses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<WorkflowClass?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.WorkflowClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<List<WorkflowClass>> ListAsync(
        Guid tenantId,
        WorkflowClassScope? scope,
        WorkflowClassStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = _context.WorkflowClasses.AsQueryable()
            .Where(wc => wc.TenantId == tenantId || wc.Scope == WorkflowClassScope.Public);

        if (scope.HasValue)
            query = query.Where(wc => wc.Scope == scope.Value);

        if (status.HasValue)
            query = query.Where(wc => wc.Status == status.Value);

        return await query.ToListAsync(cancellationToken);
    }

    public void Add(WorkflowClass workflowClass) => _context.WorkflowClasses.Add(workflowClass);

    public void Remove(WorkflowClass workflowClass) => _context.WorkflowClasses.Remove(workflowClass);
}
