using System;
using System.Collections.Generic;
using FlowOS.Events.Abstractions;

namespace FlowOS.Events.Models;

public abstract class DomainEvent : IEvent
{
    public Guid EventId { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTime Timestamp { get; private set; }
    public virtual string EventType { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public int Version { get; private set; }
    
    public Dictionary<string, string> Metadata { get; private set; }

    protected DomainEvent(Guid tenantId, string eventType, int version = 1)
    {
        EventId = Guid.NewGuid();
        TenantId = tenantId;
        Timestamp = DateTime.UtcNow;
        EventType = eventType;
        Version = version;
        Metadata = new Dictionary<string, string>();
    }

    public void SetCorrelationId(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public void AddMetadata(string key, string value)
    {
        if (Metadata.ContainsKey(key))
            Metadata[key] = value;
        else
            Metadata.Add(key, value);
    }
}
