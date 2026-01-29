using System;
using System.Collections.Generic;

namespace FlowOS.Application.DTOs.Admin;

public class AdminWorkflowDetailDto
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public string DefinitionName { get; set; } = string.Empty;
    public int Version { get; set; }
    public string CurrentStepId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } // Derived from first event or audit
    public List<AdminTimelineEventDto> Timeline { get; set; } = new();
}

public class AdminTimelineEventDto
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Summary { get; set; } = string.Empty; // Human readable
    public Dictionary<string, string> KeyData { get; set; } = new();
}
