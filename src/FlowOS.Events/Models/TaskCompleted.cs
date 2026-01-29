using System;

namespace FlowOS.Events.Models;

public class TaskCompleted : DomainEvent
{
    public Guid TaskId { get; private set; }
    public Guid CompletedBy { get; private set; }
    
    public TaskCompleted(Guid tenantId, Guid taskId, Guid completedBy) 
        : base(tenantId, "TaskCompleted")
    {
        TaskId = taskId;
        CompletedBy = completedBy;
    }

    private TaskCompleted() { }
}
