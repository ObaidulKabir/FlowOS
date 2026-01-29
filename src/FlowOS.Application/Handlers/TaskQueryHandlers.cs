using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Application.Handlers;

public class TaskQueryHandlers : 
    IRequestHandler<GetTasksQuery, List<TaskDto>>,
    IRequestHandler<GetTaskByIdQuery, TaskDto>
{
    private readonly FlowOSDbContext _context;

    public TaskQueryHandlers(FlowOSDbContext context)
    {
        _context = context;
    }

    public async Task<List<TaskDto>> Handle(GetTasksQuery request, CancellationToken cancellationToken)
    {
        // Query waiting workflows
        var query = _context.WorkflowInstances
            .AsNoTracking()
            .Where(w => w.Status == WorkflowInstanceStatus.Waiting);

        if (request.TenantId.HasValue)
        {
            query = query.Where(w => w.TenantId == request.TenantId.Value);
        }

        var workflows = await query.ToListAsync(cancellationToken);
        
        // Fetch insights for these workflows
        var workflowIds = workflows.Select(w => w.Id).ToList();
        var insights = await _context.AgentInsights
            .AsNoTracking()
            .Where(i => workflowIds.Contains(i.WorkflowInstanceId))
            .ToListAsync(cancellationToken);

        // Map to DTOs
        var result = workflows.Select(w => new TaskDto
        {
            TaskId = w.Id,
            WorkflowId = w.WorkflowDefinitionId,
            CurrentStep = w.CurrentStepId,
            Status = w.Status.ToString(),
            RequiredRole = "User", // TODO: Lookup from definition or step config
            AgentInsights = insights
                .Where(i => i.WorkflowInstanceId == w.Id)
                .Select(i => new AgentInsightDto
                {
                    AgentId = i.AgentId,
                    Insight = i.Insight,
                    ContextObjective = i.ContextObjective,
                    CreatedAt = i.CreatedAt
                })
                .ToList()
        }).ToList();

        return result;
    }

    public async Task<TaskDto> Handle(GetTaskByIdQuery request, CancellationToken cancellationToken)
    {
        var workflow = await _context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.TaskId, cancellationToken);

        if (workflow == null)
            return null; // Or throw NotFoundException

        var insights = await _context.AgentInsights
            .AsNoTracking()
            .Where(i => i.WorkflowInstanceId == request.TaskId)
            .ToListAsync(cancellationToken);

        return new TaskDto
        {
            TaskId = workflow.Id,
            WorkflowId = workflow.WorkflowDefinitionId,
            CurrentStep = workflow.CurrentStepId,
            Status = workflow.Status.ToString(),
            RequiredRole = "User", // TODO: Real lookup
            AgentInsights = insights.Select(i => new AgentInsightDto
            {
                AgentId = i.AgentId,
                Insight = i.Insight,
                ContextObjective = i.ContextObjective,
                CreatedAt = i.CreatedAt
            }).ToList()
        };
    }
}
