using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetPublishedByNameAndVersionAsync(string name, int version, Guid tenantId, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetLatestByNameAsync(string name, Guid tenantId, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetByNameAndVersionAsync(string name, int version, Guid tenantId, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetAnyByNameAsync(string name, Guid tenantId, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, string>> GetNamesByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    void Add(WorkflowDefinition definition);
}
