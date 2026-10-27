using System;
using System.Collections.Generic;

namespace FlowOS.Application.DTOs;

public class TaskDto
{
    public Guid TaskId { get; set; }
    public Guid WorkflowId { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public string RequiredRole { get; set; } = string.Empty;
    public List<string> RequiredRoles { get; set; } = new();
    public string? AssigneeId { get; set; }
    public Dictionary<string, string> RoleAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Status { get; set; } = string.Empty;
    /// <summary>CR-12: effective priority (instance override, else step, else 0); higher is more urgent.</summary>
    public int Priority { get; set; }
    /// <summary>CR-12: when the waiting step's SLA times out, if it has one.</summary>
    public DateTime? DueAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
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
