using System;

namespace FlowOS.Notifications.Domain;

public class Notification
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? CorrelationId { get; private set; } // WorkflowInstanceId or similar
    public string EventType { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string Severity { get; private set; } = "Info"; // Info, Warning, Critical
    public DateTime CreatedAt { get; private set; }

    protected Notification() { }

    public Notification(Guid tenantId, string eventType, string message, string severity, Guid? correlationId)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        EventType = eventType;
        Message = message;
        Severity = severity;
        CorrelationId = correlationId;
        CreatedAt = DateTime.UtcNow;
    }
}
