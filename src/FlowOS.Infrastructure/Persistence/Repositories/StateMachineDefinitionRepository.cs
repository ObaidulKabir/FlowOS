using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class StateMachineDefinitionRepository : IStateMachineDefinitionRepository
{
    private readonly FlowOSDbContext _context;

    public StateMachineDefinitionRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<StateMachineDefinition?> GetByEntityTypeAndTenantAsync(string entityType, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.StateMachineDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.EntityType == entityType && s.TenantId == tenantId, cancellationToken);

    public Task<StateMachineDefinition?> GetByEntityTypeAsync(string entityType, CancellationToken cancellationToken = default)
        => _context.StateMachineDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(sm => sm.EntityType == entityType, cancellationToken);

    public Task<List<StateMachineDefinition>> ListAllAsync(CancellationToken cancellationToken = default)
        => _context.StateMachineDefinitions.AsNoTracking().ToListAsync(cancellationToken);
}
