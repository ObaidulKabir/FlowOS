using System;

namespace FlowOS.Application.DTOs.Admin;

public class AdminWorkflowSummaryDto
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public string DefinitionName { get; set; } = string.Empty;
    public int Version { get; set; }
    public string CurrentStepId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? CorrelationId { get; set; }
}
