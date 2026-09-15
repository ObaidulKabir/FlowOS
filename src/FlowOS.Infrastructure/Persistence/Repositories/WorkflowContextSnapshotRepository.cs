using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class WorkflowContextSnapshotRepository : IWorkflowContextSnapshotRepository
{
    private readonly FlowOSDbContext _context;

    public WorkflowContextSnapshotRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<WorkflowContextSnapshot?> GetAsync(
        Guid workflowInstanceId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => _context.WorkflowContextSnapshots
            .FirstOrDefaultAsync(
                x => x.WorkflowInstanceId == workflowInstanceId && x.TenantId == tenantId,
                cancellationToken);

    public Task<WorkflowContextSnapshot?> GetAsNoTrackingAsync(
        Guid workflowInstanceId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => _context.WorkflowContextSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.WorkflowInstanceId == workflowInstanceId && x.TenantId == tenantId,
                cancellationToken);

    public void Add(WorkflowContextSnapshot snapshot) => _context.WorkflowContextSnapshots.Add(snapshot);
}
