using System;

namespace FlowOS.Events.Models;

public class StandardEvent : DomainEvent
{
    // No override needed if base property is virtual but we want to set it via base constructor
    // Actually, DomainEvent.EventType has a private set.
    // So we can just rely on the base constructor setting it.
    
    public StandardEvent(Guid tenantId, string eventType) : base(tenantId, eventType)
    {
    }
}
