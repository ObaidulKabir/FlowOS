using System;

namespace FlowOS.Events.Abstractions;

public interface IEvent
{
    Guid EventId { get; }
    Guid TenantId { get; }
    DateTime Timestamp { get; }
    string EventType { get; }
    Guid? CorrelationId { get; }
    int Version { get; }
}
