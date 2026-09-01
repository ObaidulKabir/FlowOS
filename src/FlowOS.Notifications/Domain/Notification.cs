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

    public Guid? TargetUserId { get; private set; }
    public bool IsRead { get; private set; }

    protected Notification() { }

    public Notification(Guid tenantId, string eventType, string message, string severity, Guid? correlationId, Guid? targetUserId = null)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        EventType = eventType;
        Message = message;
        Severity = severity;
        CorrelationId = correlationId;
        TargetUserId = targetUserId;
        CreatedAt = DateTime.UtcNow;
        IsRead = false;
    }

    public void MarkAsRead()
    {
        IsRead = true;
    }
}
