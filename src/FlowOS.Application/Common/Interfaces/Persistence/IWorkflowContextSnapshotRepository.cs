using FlowOS.Domain.Entities;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IWorkflowContextSnapshotRepository
{
    Task<WorkflowContextSnapshot?> GetAsync(Guid workflowInstanceId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<WorkflowContextSnapshot?> GetAsNoTrackingAsync(Guid workflowInstanceId, Guid tenantId, CancellationToken cancellationToken = default);
    void Add(WorkflowContextSnapshot snapshot);
}
