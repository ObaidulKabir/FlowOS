using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class WorkflowContextBindingRepository : IWorkflowContextBindingRepository
{
    private readonly FlowOSDbContext _context;

    public WorkflowContextBindingRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<WorkflowContextBinding?> GetByIdAsync(
        Guid id,
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => _context.WorkflowContextBindings
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);

    public Task<WorkflowContextBinding?> GetByIdAsNoTrackingAsync(
        Guid id,
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => _context.WorkflowContextBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);

    public Task<WorkflowContextBinding?> GetByContextTypeAsync(
        string contextType,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var normalized = contextType.Trim().ToUpperInvariant();
        return _context.WorkflowContextBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.NormalizedContextType == normalized,
                cancellationToken);
    }

    public Task<WorkflowContextBinding?> GetByNameAsync(
        string name,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return _context.WorkflowContextBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.NormalizedName == normalized,
                cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowContextBinding>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => await _context.WorkflowContextBindings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkflowContextBindingRevision>> ListRevisionsAsync(
        IEnumerable<Guid> bindingIds,
        CancellationToken cancellationToken = default)
    {
        var ids = bindingIds.Distinct().ToList();
        return await _context.WorkflowContextBindingRevisions
            .AsNoTracking()
            .Where(x => ids.Contains(x.BindingId))
            .OrderByDescending(x => x.Revision)
            .ToListAsync(cancellationToken);
    }

    public Task<WorkflowContextBindingRevision?> GetRevisionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _context.WorkflowContextBindingRevisions
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<WorkflowContextBindingRevision?> GetRevisionByIdAsNoTrackingAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _context.WorkflowContextBindingRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<WorkflowContextBindingRevision?> GetRevisionByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => (from revision in _context.WorkflowContextBindingRevisions.AsNoTracking()
            join binding in _context.WorkflowContextBindings.AsNoTracking()
                on revision.BindingId equals binding.Id
            where revision.WorkflowDefinitionId == workflowDefinitionId && binding.TenantId == tenantId
            select revision)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> GetNextRevisionNumberAsync(
        Guid bindingId,
        CancellationToken cancellationToken = default)
    {
        var maximum = await _context.WorkflowContextBindingRevisions
            .Where(x => x.BindingId == bindingId)
            .Select(x => (int?)x.Revision)
            .MaxAsync(cancellationToken);
        return (maximum ?? 0) + 1;
    }

    public void Add(WorkflowContextBinding binding) => _context.WorkflowContextBindings.Add(binding);

    public void AddRevision(WorkflowContextBindingRevision revision)
        => _context.WorkflowContextBindingRevisions.Add(revision);
}
