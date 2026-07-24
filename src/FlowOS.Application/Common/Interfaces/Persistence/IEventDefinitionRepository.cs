using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IEventDefinitionRepository
{
    Task<List<EventDefinition>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<EventDefinition?> GetByEventIdAndTenantAsync(string eventId, Guid tenantId, CancellationToken cancellationToken = default);
    void Add(EventDefinition eventDefinition);
}
