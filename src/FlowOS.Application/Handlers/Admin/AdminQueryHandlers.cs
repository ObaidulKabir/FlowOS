using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Events;
using FlowOS.Application.DTOs.Admin;
using FlowOS.Application.Queries.Admin;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Application.Handlers.Admin;

public class AdminQueryHandlers :
    IRequestHandler<GetAdminWorkflowDetailQuery, AdminWorkflowDetailDto>
{
    private readonly FlowOSDbContext _context;

    public AdminQueryHandlers(FlowOSDbContext context)
    {
        _context = context;
    }

    public async Task<AdminWorkflowDetailDto> Handle(GetAdminWorkflowDetailQuery request, CancellationToken cancellationToken)
    {
        var instance = await _context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WorkflowInstanceId && w.TenantId == request.TenantId, cancellationToken);

        if (instance == null) return null;

        var definition = await _context.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == instance.WorkflowDefinitionId, cancellationToken);

        // Fetch Raw Events - In a real system this might be a separate event store query
        // We use correlation ID or explicit linking. 
        // For Phase 1-6, we assume events have CorrelationId = WorkflowInstanceId or similar.
        // EF Core 8+ can handle simple IS checks but complexities arise. We fetch by correlation ID first.
        var events = await _context.Events
            .AsNoTracking()
            .Where(e => e.CorrelationId == request.WorkflowInstanceId)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);
            
        // Also try to find TaskCompleted events if they are not correlated (though they should be)
        // For this demo, we assume strict correlation.

        // Map to Timeline
        var timeline = events.Select(e => MapToTimeline(e)).ToList();

        return new AdminWorkflowDetailDto
        {
            Id = instance.Id,
            DefinitionId = instance.WorkflowDefinitionId,
            DefinitionName = definition?.Name ?? "Unknown",
            Version = instance.WorkflowVersion,
            CurrentStepId = instance.CurrentStepId,
            Status = instance.Status.ToString(),
            CorrelationId = instance.CorrelationId,
            Timeline = timeline
        };
    }

    private AdminTimelineEventDto MapToTimeline(DomainEvent evt)
    {
        var summary = "System event recorded";
        var keyData = new Dictionary<string, string>();

        switch (evt)
        {
            case AgentInsightGenerated aig:
                summary = $"Agent {aig.AgentId} suggested: {aig.Insight}";
                keyData.Add("Agent", aig.AgentId);
                keyData.Add("Objective", aig.ContextObjective);
                break;
            case TaskCompleted tc:
                summary = "Task completed by user";
                keyData.Add("TaskId", tc.TaskId.ToString());
                keyData.Add("UserId", tc.CompletedBy.ToString());
                break;
            default:
                summary = $"Event: {evt.EventType}";
                break;
        }

        return new AdminTimelineEventDto
        {
            EventId = evt.EventId,
            EventType = evt.EventType,
            Timestamp = evt.Timestamp,
            Summary = summary,
            KeyData = keyData
        };
    }
}
