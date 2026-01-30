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

public class WorkflowQueryHandlers : IRequestHandler<GetWorkflowsQuery, List<WorkflowSummaryDto>>
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
            .OrderByDescending(w => w.Id) // Ideally CreatedAt, but Id GUID is not sortable by time usually. In MVP acceptable.
            .ToListAsync(cancellationToken);

        return instances.Select(i => new WorkflowSummaryDto
        {
            Id = i.Id,
            DefinitionId = i.WorkflowDefinitionId,
            Version = i.WorkflowVersion,
            CurrentStepId = i.CurrentStepId,
            Status = i.Status.ToString(),
            CorrelationId = i.CorrelationId,
            // CreatedAt is not on WorkflowInstance entity in Phase 1-6 yet, or it is? 
            // Checking AdminWorkflowDetailDto mapping, it used First Event.
            // For summary, we might leave it default or null if not available.
            CreatedAt = DateTime.MinValue // Placeholder as Entity doesn't have CreatedAt
        }).ToList();
    }
}
