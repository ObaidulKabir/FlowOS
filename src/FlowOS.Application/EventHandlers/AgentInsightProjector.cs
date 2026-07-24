using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Events;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.ReadModels;
using FlowOS.Core.Common.Models;
using MediatR;

namespace FlowOS.Application.EventHandlers;

public class AgentInsightProjector : INotificationHandler<DomainEventNotification<AgentInsightGenerated>>
{
    private readonly IUnitOfWork _unitOfWork;

    public AgentInsightProjector(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(DomainEventNotification<AgentInsightGenerated> notification, CancellationToken cancellationToken)
    {
        var evt = notification.DomainEvent;

        var readModel = new AgentInsightReadModel
        {
            Id = Guid.NewGuid(),
            TenantId = evt.TenantId,
            WorkflowInstanceId = evt.CorrelationId ?? Guid.Empty,
            AgentId = evt.AgentId,
            Insight = evt.Insight,
            ContextObjective = evt.ContextObjective,
            CreatedAt = evt.Timestamp
        };

        _unitOfWork.AgentInsights.Add(readModel);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
