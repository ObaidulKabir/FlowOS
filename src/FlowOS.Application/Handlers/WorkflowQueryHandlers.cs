using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Application.Handlers;

public class WorkflowQueryHandlers : 
    IRequestHandler<GetWorkflowsQuery, List<WorkflowSummaryDto>>,
    IRequestHandler<GetWorkflowByIdQuery, WorkflowSummaryDto?>
{
    private readonly FlowOSDbContext _context;

    public WorkflowQueryHandlers(FlowOSDbContext context)
    {
        _context = context;
    }

    public async Task<List<WorkflowSummaryDto>> Handle(GetWorkflowsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.WorkflowInstances
            .AsNoTracking()
            .Where(w => w.TenantId == request.TenantId);

        if (request.Status.HasValue)
        {
            query = query.Where(w => w.Status == request.Status.Value);
        }

        var instances = await query
            .OrderByDescending(w => w.Id) 
            .ToListAsync(cancellationToken);

        return instances.Select(MapToDto).ToList();
    }

    public async Task<WorkflowSummaryDto?> Handle(GetWorkflowByIdQuery request, CancellationToken cancellationToken)
    {
        var instance = await _context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.Id && w.TenantId == request.TenantId, cancellationToken);

        return instance == null ? null : MapToDto(instance);
    }

    private static WorkflowSummaryDto MapToDto(FlowOS.Workflows.Domain.WorkflowInstance i)
    {
        return new WorkflowSummaryDto
        {
            Id = i.Id,
            DefinitionId = i.WorkflowDefinitionId,
            Version = i.WorkflowVersion,
            CurrentStepId = i.CurrentStepId,
            Status = i.Status.ToString(),
            CorrelationId = i.CorrelationId,
            CreatedAt = DateTime.MinValue
        };
    }
}
