using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FlowOS.Application.Commands;
using FlowOS.Core.Common.Models; // For DomainEventNotification
using FlowOS.Infrastructure.Persistence;
using FlowOS.Agents.Events;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Application.Handlers;

public class AgentCommandHandlers : IRequestHandler<PublishAgentInsightCommand, bool>
{
    private readonly FlowOSDbContext _context;
    private readonly IPublisher _publisher;

    public AgentCommandHandlers(FlowOSDbContext context, IPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task<bool> Handle(PublishAgentInsightCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify Workflow Instance exists
        var instance = await _context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WorkflowInstanceId && w.TenantId == request.TenantId, cancellationToken);

        if (instance == null) return false;

        // 2. Create AgentInsightGenerated Event
        var domainEvent = new AgentInsightGenerated(
            request.TenantId,
            request.AgentId,
            request.Insight,
            request.ContextObjective
        );

        // 3. Correlate
        domainEvent.SetCorrelationId(request.CorrelationId ?? request.WorkflowInstanceId);

        // 4. Persist to Write Model (Events Table)
        _context.Events.Add(domainEvent);
        await _context.SaveChangesAsync(cancellationToken);
        
        // 5. Publish Notification for Read Models (Projectors)
        // Since FlowOSDbContext doesn't auto-dispatch yet, we do it manually here.
        await _publisher.Publish(new DomainEventNotification<AgentInsightGenerated>(domainEvent), cancellationToken);
        
        return true;
    }
}
