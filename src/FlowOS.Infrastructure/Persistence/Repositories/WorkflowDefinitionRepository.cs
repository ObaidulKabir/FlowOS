using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class WorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private readonly FlowOSDbContext _context;

    public WorkflowDefinitionRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<WorkflowDefinition?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<WorkflowDefinition?> GetPublishedByNameAndVersionAsync(string name, int version, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Name == name
                && w.Version == version
                && w.TenantId == tenantId
                && w.Status == WorkflowStatus.Published, cancellationToken);

    public Task<WorkflowDefinition?> GetLatestByNameAsync(string name, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.WorkflowDefinitions
            .AsNoTracking()
            .Where(w => w.Name == name && w.TenantId == tenantId)
            .OrderByDescending(w => w.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<WorkflowDefinition?> GetByNameAndVersionAsync(string name, int version, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Name == name && d.Version == version && d.TenantId == tenantId, cancellationToken);

    public Task<WorkflowDefinition?> GetAnyByNameAsync(string name, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Name == name && d.TenantId == tenantId, cancellationToken);

    public Task<Dictionary<Guid, string>> GetNamesByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        => _context.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

    public void Add(WorkflowDefinition definition) => _context.WorkflowDefinitions.Add(definition);
}
