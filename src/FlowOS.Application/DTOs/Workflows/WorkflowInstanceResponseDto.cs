using System;

namespace FlowOS.Application.DTOs.Workflows;

public class WorkflowInstanceResponseDto
{
    public Guid WorkflowId { get; set; }
    public Guid WorkflowClassId { get; set; }
    public string WorkflowClassName { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
