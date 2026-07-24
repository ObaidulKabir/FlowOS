using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class DomainEventRepository : IDomainEventRepository
{
    private readonly FlowOSDbContext _context;

    public DomainEventRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<List<DomainEvent>> ListByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken = default)
        => _context.Events
            .AsNoTracking()
            .Where(e => e.CorrelationId == correlationId)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);

    public void Add(DomainEvent domainEvent) => _context.Events.Add(domainEvent);
}
