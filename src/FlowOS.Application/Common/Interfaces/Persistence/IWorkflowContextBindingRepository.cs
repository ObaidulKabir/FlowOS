using FlowOS.Domain.Entities;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IWorkflowContextBindingRepository
{
    Task<WorkflowContextBinding?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<WorkflowContextBinding?> GetByIdAsNoTrackingAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<WorkflowContextBinding?> GetByContextTypeAsync(string contextType, Guid tenantId, CancellationToken cancellationToken = default);
    Task<WorkflowContextBinding?> GetByNameAsync(string name, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowContextBinding>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowContextBindingRevision>> ListRevisionsAsync(IEnumerable<Guid> bindingIds, CancellationToken cancellationToken = default);
    Task<WorkflowContextBindingRevision?> GetRevisionByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowContextBindingRevision?> GetRevisionByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowContextBindingRevision?> GetRevisionByWorkflowDefinitionIdAsync(Guid workflowDefinitionId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<int> GetNextRevisionNumberAsync(Guid bindingId, CancellationToken cancellationToken = default);
    void Add(WorkflowContextBinding binding);
    void AddRevision(WorkflowContextBindingRevision revision);
}
