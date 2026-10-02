using System;

namespace FlowOS.Domain.Entities;

public class ConversationMessageRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public string StepId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // System, User, Assistant, Tool
    public string Content { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
