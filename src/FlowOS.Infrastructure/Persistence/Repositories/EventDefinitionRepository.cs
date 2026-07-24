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

public class EventDefinitionRepository : IEventDefinitionRepository
{
    private readonly FlowOSDbContext _context;

    public EventDefinitionRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<List<EventDefinition>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _context.EventDefinitions
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.EventId)
            .ToListAsync(cancellationToken);

    public Task<EventDefinition?> GetByEventIdAndTenantAsync(string eventId, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.EventDefinitions
            .FirstOrDefaultAsync(e => e.EventId == eventId && e.TenantId == tenantId, cancellationToken);

    public void Add(EventDefinition eventDefinition) => _context.EventDefinitions.Add(eventDefinition);
}
