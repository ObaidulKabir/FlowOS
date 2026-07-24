using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Models;
using FlowOS.Agents.Events;

namespace FlowOS.Application.Handlers;

public class AgentCommandHandlers : IRequestHandler<PublishAgentInsightCommand, bool>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public AgentCommandHandlers(IUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<bool> Handle(PublishAgentInsightCommand request, CancellationToken cancellationToken)
    {
        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsNoTrackingAsync(request.WorkflowInstanceId, request.TenantId, cancellationToken);

        if (instance == null) return false;

        var domainEvent = new AgentInsightGenerated(
            request.TenantId,
            request.AgentId,
            request.Insight,
            request.ContextObjective
        );

        domainEvent.SetCorrelationId(request.CorrelationId ?? request.WorkflowInstanceId);

        _unitOfWork.Events.Add(domainEvent);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        await _publisher.Publish(new DomainEventNotification<AgentInsightGenerated>(domainEvent), cancellationToken);
        
        return true;
    }
}
