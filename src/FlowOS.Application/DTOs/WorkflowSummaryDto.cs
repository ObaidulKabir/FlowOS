using System;

namespace FlowOS.Application.DTOs;

public class WorkflowSummaryDto
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    
    // UI expects these properties instead of just DefinitionId
    public Guid WorkflowId { get; set; }
    public Guid WorkflowClassId { get; set; }
    public string WorkflowClassName { get; set; }

    public int Version { get; set; }
    public string CurrentStepId { get; set; } = string.Empty;
    public string CurrentStep { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
