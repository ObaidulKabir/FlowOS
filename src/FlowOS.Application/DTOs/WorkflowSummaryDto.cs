using System;

namespace FlowOS.Application.DTOs;

public class WorkflowSummaryDto
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public int Version { get; set; }
    public string CurrentStepId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
}
