using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.DTOs;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstance?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetByIdAsNoTrackingAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<WorkflowInstance>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<List<WorkflowInstance>> ListByStatusAsync(WorkflowInstanceStatus status, Guid? tenantId, CancellationToken cancellationToken = default);
    Task<List<WorkflowSummaryDto>> GetSummariesByTenantAsync(Guid tenantId, WorkflowInstanceStatus? status, CancellationToken cancellationToken = default);
    Task<WorkflowSummaryDto?> GetSummaryByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> AnyForWorkflowClassAsync(Guid workflowClassId, CancellationToken cancellationToken = default);
    void Add(WorkflowInstance instance);
}
