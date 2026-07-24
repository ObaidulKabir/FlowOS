using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IWorkflowClassRepository
{
    Task<WorkflowClass?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowClass?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<WorkflowClass>> ListAsync(Guid tenantId, WorkflowClassScope? scope, WorkflowClassStatus? status, CancellationToken cancellationToken = default);
    void Add(WorkflowClass workflowClass);
    void Remove(WorkflowClass workflowClass);
}
