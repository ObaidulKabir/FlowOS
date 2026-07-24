using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Events.Models;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IDomainEventRepository
{
    Task<List<DomainEvent>> ListByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken = default);
    void Add(DomainEvent domainEvent);
}
