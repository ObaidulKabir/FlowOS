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

    public Task<List<DomainEvent>> ListByTenantAsync(Guid tenantId, Guid? correlationId = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        var query = _context.Events
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId);

        if (correlationId.HasValue && correlationId.Value != Guid.Empty)
        {
            query = query.Where(e => e.CorrelationId == correlationId.Value);
        }

        return query
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<List<DomainEvent>> ListForAgentEvaluationAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> correlationIds,
        DateTime fromUtc,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (correlationIds.Count == 0)
            return Task.FromResult(new List<DomainEvent>());

        var ids = correlationIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
            return Task.FromResult(new List<DomainEvent>());

        var normalizedFrom = fromUtc.Kind switch
        {
            DateTimeKind.Utc => fromUtc,
            DateTimeKind.Local => fromUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc)
        };

        return _context.Events
            .AsNoTracking()
            .Where(e =>
                e.TenantId == tenantId &&
                e.CorrelationId.HasValue &&
                ids.Contains(e.CorrelationId.Value) &&
                e.Timestamp >= normalizedFrom)
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.EventId)
            .ToListAsync(cancellationToken);
    }

    public void Add(DomainEvent domainEvent) => _context.Events.Add(domainEvent);
}
