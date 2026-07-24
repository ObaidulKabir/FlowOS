using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Application.Queries.Governance;
using FlowOS.Domain.Enums;
using MediatR;

namespace FlowOS.Application.Handlers.Governance;

public class WorkflowClassQueryHandlers :
    IRequestHandler<ListWorkflowClassesQuery, List<WorkflowClassResponseDto>>,
    IRequestHandler<GetWorkflowClassByIdQuery, WorkflowClassResponseDto?>
{
    private readonly IUnitOfWork _unitOfWork;

    public WorkflowClassQueryHandlers(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<WorkflowClassResponseDto>> Handle(ListWorkflowClassesQuery request, CancellationToken cancellationToken)
    {
        var list = await _unitOfWork.WorkflowClasses.ListAsync(
            request.TenantId, request.Scope, request.Status, cancellationToken);

        return list.Select(WorkflowClassCommandHandlers.MapToDto).ToList();
    }

    public async Task<WorkflowClassResponseDto?> Handle(GetWorkflowClassByIdQuery request, CancellationToken cancellationToken)
    {
        var wc = await _unitOfWork.WorkflowClasses.GetByIdAsNoTrackingAsync(request.Id, cancellationToken);
        if (wc == null) return null;

        if (wc.Scope == WorkflowClassScope.Private && wc.TenantId != request.TenantId)
            throw new UnauthorizedAccessException("WorkflowClass is private to another tenant.");

        return WorkflowClassCommandHandlers.MapToDto(wc);
    }
}
