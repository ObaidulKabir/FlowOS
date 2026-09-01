using System;

namespace FlowOS.Core.Common.Models;

public class WorkflowTimerJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string StepId { get; private set; } = string.Empty;
    public string TriggerEventType { get; private set; } = string.Empty;
    public DateTime DueTimeUtc { get; private set; }
    public bool IsProcessed { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    protected WorkflowTimerJob() { }

    public WorkflowTimerJob(Guid tenantId, Guid workflowInstanceId, string stepId, string triggerEventType, DateTime dueTimeUtc)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        WorkflowInstanceId = workflowInstanceId;
        StepId = stepId;
        TriggerEventType = triggerEventType;
        DueTimeUtc = dueTimeUtc;
        IsProcessed = false;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkAsProcessed()
    {
        IsProcessed = true;
        ProcessedAt = DateTime.UtcNow;
    }
}
