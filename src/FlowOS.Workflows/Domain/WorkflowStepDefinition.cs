using System.Collections.Generic;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Domain;

public class WorkflowStepDefinition
{
    public string StepId { get; set; } = string.Empty;
    public WorkflowStepType StepType { get; set; }
    public List<string> AllowedRoles { get; set; } = new();
    
    // Maps EventType (or EventId) -> NextStepId
    // In Phase 1: Keys can be either legacy string events or new EventIds
    public Dictionary<string, string> NextSteps { get; set; } = new();

    public WorkflowStepDefinition() { }

    public WorkflowStepDefinition(string stepId, WorkflowStepType type)
    {
        StepId = stepId;
        StepType = type;
    }
}
