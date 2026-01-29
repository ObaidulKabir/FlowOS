using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Events;
using FlowOS.Application.Common.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.ReadModels;
using MediatR;

namespace FlowOS.Application.EventHandlers;

public class AgentInsightProjector : INotificationHandler<DomainEventNotification<AgentInsightGenerated>>
{
    private readonly FlowOSDbContext _context;

    public AgentInsightProjector(FlowOSDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DomainEventNotification<AgentInsightGenerated> notification, CancellationToken cancellationToken)
    {
        var evt = notification.DomainEvent;

        // Create Read Model entry
        var readModel = new AgentInsightReadModel
        {
            Id = Guid.NewGuid(),
            TenantId = evt.TenantId,
            WorkflowInstanceId = evt.CorrelationId ?? Guid.Empty, // Assuming CorrelationId is the WorkflowInstanceId
            AgentId = evt.AgentId,
            Insight = evt.Insight,
            ContextObjective = evt.ContextObjective,
            CreatedAt = evt.Timestamp
        };

        // Note: In a real system, we might want to ensure idempotency here
        _context.AgentInsights.Add(readModel);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
