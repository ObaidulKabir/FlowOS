using System.Collections.Generic;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Domain;

public class WorkflowStepDefinition
{
    public string StepId { get; set; } = string.Empty;
    public WorkflowStepType StepType { get; set; }
    // Optional: Roles allowed to execute this step (for Command/UserTask steps)
    public List<string> AllowedRoles { get; set; } = new();
    
    // Maps EventType (or EventId) -> NextStepId
    // In Phase 1: Keys can be either legacy string events or new EventIds
    public Dictionary<string, string> NextSteps { get; set; } = new();

    // Conditional Logic (For Decision Steps)
    // Key: Condition Expression (e.g., "Payload.Amount > 100")
    // Value: NextStepId
    public Dictionary<string, string> Conditions { get; set; } = new();

    // Declarative Step SLA / Boundary Timer & Escalation
    public StepSlaDefinition? Sla { get; set; }

    // Declarative Lifecycle Actions (Pre/Post Event & Step Hooks)
    public List<StepActionDefinition> OnEntry { get; set; } = new();
    public List<StepActionDefinition> OnExit { get; set; } = new();

    public WorkflowStepDefinition() { }

    public WorkflowStepDefinition(string stepId, WorkflowStepType type)
    {
        StepId = stepId;
        StepType = type;
    }
}

public class StepActionDefinition
{
    public string ActionType { get; set; } = "Notification";
    public string? Target { get; set; }
    public string? Url { get; set; }
    public string? Method { get; set; } = "POST";
    public string? Template { get; set; }
    public Dictionary<string, string>? PayloadMapping { get; set; }
    public string? Condition { get; set; }

    public StepActionDefinition() { }

    public StepActionDefinition(string actionType)
    {
        ActionType = actionType;
    }
}
