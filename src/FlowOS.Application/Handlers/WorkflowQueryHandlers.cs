using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using MediatR;

namespace FlowOS.Application.Handlers;

public class WorkflowQueryHandlers : 
    IRequestHandler<GetWorkflowsQuery, List<WorkflowSummaryDto>>,
    IRequestHandler<GetWorkflowByIdQuery, WorkflowSummaryDto?>
{
    private readonly IUnitOfWork _unitOfWork;

    public WorkflowQueryHandlers(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<List<WorkflowSummaryDto>> Handle(GetWorkflowsQuery request, CancellationToken cancellationToken)
        => _unitOfWork.WorkflowInstances.GetSummariesByTenantAsync(request.TenantId, request.Status, cancellationToken);

    public Task<WorkflowSummaryDto?> Handle(GetWorkflowByIdQuery request, CancellationToken cancellationToken)
        => _unitOfWork.WorkflowInstances.GetSummaryByIdAsync(request.Id, request.TenantId, cancellationToken);
}
