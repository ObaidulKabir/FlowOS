using System;
using System.Collections.Generic;

namespace FlowOS.Application.DTOs;

public class TaskDto
{
    public Guid TaskId { get; set; }
    public Guid WorkflowId { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public string RequiredRole { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public object? RelatedEntity { get; set; }
    public List<AgentInsightDto> AgentInsights { get; set; } = new();
}

public class AgentInsightDto
{
    public string AgentId { get; set; } = string.Empty;
    public string Insight { get; set; } = string.Empty;
    public string ContextObjective { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
