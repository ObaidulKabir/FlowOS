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
        var query = from w in _context.WorkflowInstances
                    join wc in _context.WorkflowClasses on w.WorkflowClassId equals wc.Id
                    where w.TenantId == request.TenantId
                    select new { w, wc.Name };

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.w.Status == request.Status.Value);
        }

        var instances = await query
            .OrderByDescending(x => x.w.CreatedAt) 
            .ToListAsync(cancellationToken);

        return instances.Select(x => new WorkflowSummaryDto
        {
            Id = x.w.Id,
            WorkflowId = x.w.Id,
            WorkflowClassId = x.w.WorkflowClassId,
            WorkflowClassName = x.Name,
            DefinitionId = x.w.WorkflowDefinitionId,
            Version = x.w.WorkflowVersion,
            CurrentStepId = x.w.CurrentStepId,
            CurrentStep = x.w.CurrentStepId,
            Status = x.w.Status.ToString(),
            CorrelationId = x.w.CorrelationId,
            CreatedAt = x.w.CreatedAt,
            CompletedAt = x.w.CompletedAt
        }).ToList();
    }

    public async Task<WorkflowSummaryDto?> Handle(GetWorkflowByIdQuery request, CancellationToken cancellationToken)
    {
        var result = await (from w in _context.WorkflowInstances
                            join wc in _context.WorkflowClasses on w.WorkflowClassId equals wc.Id
                            where w.Id == request.Id && w.TenantId == request.TenantId
                            select new WorkflowSummaryDto
                            {
                                Id = w.Id,
                                WorkflowId = w.Id,
                                WorkflowClassId = w.WorkflowClassId,
                                WorkflowClassName = wc.Name,
                                DefinitionId = w.WorkflowDefinitionId,
                                Version = w.WorkflowVersion,
                                CurrentStepId = w.CurrentStepId,
                                CurrentStep = w.CurrentStepId,
                                Status = w.Status.ToString(),
                                CorrelationId = w.CorrelationId,
                                CreatedAt = w.CreatedAt,
                                CompletedAt = w.CompletedAt
                            }).FirstOrDefaultAsync(cancellationToken);

        return result;
    }
}
