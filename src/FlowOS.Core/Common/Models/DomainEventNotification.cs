using FlowOS.Events.Abstractions;
using MediatR;

namespace FlowOS.Core.Common.Models;

public class DomainEventNotification<TDomainEvent> : INotification 
    where TDomainEvent : IEvent
{
    public TDomainEvent DomainEvent { get; }

    public DomainEventNotification(TDomainEvent domainEvent)
    {
        DomainEvent = domainEvent;
    }
}
