using System.Collections.Generic;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Domain;

public class WorkflowStepDefinition
{
    public string StepId { get; set; } = string.Empty;
    public WorkflowStepType StepType { get; set; }
    public string? DecisionProvider { get; set; }
    public SubWorkflowReferenceDefinition? SubWorkflow { get; set; }
    // Optional: Roles allowed to execute this step (for Command/UserTask steps)
    public List<string> AllowedRoles { get; set; } = new();
    
    // Maps EventType (or EventId) -> NextStepId
    // In Phase 1: Keys can be either legacy string events or new EventIds
    public Dictionary<string, string> NextSteps { get; set; } = new();

    /// <summary>
    /// Per-event travel caps for repeatable paths. Key matches a NextSteps or Decision Conditions key.
    /// </summary>
    public Dictionary<string, PathTravelLimit> PathLimits { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Conditional Logic (For Decision Steps)
    // Key: Condition Expression (e.g., "Payload.Amount > 100")
    // Value: NextStepId
    public Dictionary<string, string> Conditions { get; set; } = new();

    // For Parallel Fork/Join steps
    public List<string> Branches { get; set; } = new();
    public string JoinPolicy { get; set; } = "WaitAll"; // "WaitAll" or "WaitAny"
    public List<string> InboundSteps { get; set; } = new();

    // Declarative Step SLA / Boundary Timer & Escalation
    public StepSlaDefinition? Sla { get; set; }

    public string Actor { get; set; } = "Human";
    public string? DecisionGuideline { get; set; }
    public StepAutoCommitDefinition? AutoCommit { get; set; }
    public string? AgentProvider { get; set; }
    public string? AgentPrompt { get; set; }
    public List<string> AgentTools { get; set; } = new();

    // Declarative Lifecycle Actions (Pre/Post Event & Step Hooks, Saga Rollback)
    public List<StepActionDefinition> OnEntry { get; set; } = new();
    public List<StepActionDefinition> OnExit { get; set; } = new();
    public List<StepActionDefinition> OnFailure { get; set; } = new();

    public WorkflowStepDefinition() { }

    public WorkflowStepDefinition(string stepId, WorkflowStepType type)
    {
        StepId = stepId;
        StepType = type;
    }
}

public class PathTravelLimit
{
    public int MaxTravels { get; set; } = 5;
    public string? OnExceeded { get; set; }

    public PathTravelLimit() { }

    public PathTravelLimit(int maxTravels, string? onExceeded = null)
    {
        MaxTravels = maxTravels;
        OnExceeded = onExceeded;
    }
}

public class SubWorkflowReferenceDefinition
{
    public Guid? WorkflowDefinitionId { get; set; }
    public Guid? WorkflowClassId { get; set; }
    public string? WorkflowName { get; set; }
    public int? Version { get; set; }
    public Dictionary<string, string> InputMapping { get; set; } = new();
    public Dictionary<string, string> OutputMapping { get; set; } = new();
}

public class StepActionDefinition
{
    public string ActionType { get; set; } = "Notification";
    public string? Target { get; set; }
    public string? Capability { get; set; }
    public string? Url { get; set; }
    public string? Method { get; set; } = "POST";
    public string? Template { get; set; }
    public Dictionary<string, string>? PayloadMapping { get; set; }
    public string? Condition { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public bool SignPayload { get; set; } = true;
    public string? SecretName { get; set; }

    public StepActionDefinition() { }

    public StepActionDefinition(string actionType)
    {
        ActionType = actionType;
    }
}
