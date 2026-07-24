using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IStateMachineDefinitionRepository
{
    Task<StateMachineDefinition?> GetByEntityTypeAndTenantAsync(string entityType, Guid tenantId, CancellationToken cancellationToken = default);
    Task<StateMachineDefinition?> GetByEntityTypeAsync(string entityType, CancellationToken cancellationToken = default);
    Task<List<StateMachineDefinition>> ListAllAsync(CancellationToken cancellationToken = default);
}
