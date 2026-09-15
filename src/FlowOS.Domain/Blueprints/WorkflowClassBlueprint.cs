using System;
using System.Collections.Generic;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Blueprints;

// Root Configuration Pack
public record WorkflowClassBlueprint
{
    public List<EventBlueprint> Events { get; init; } = new();
    public StateMachineBlueprint StateMachine { get; init; } = new();
    public WorkflowBlueprint Workflow { get; init; } = new();
    public List<RoleBlueprint> Roles { get; init; } = new();
    public List<CapabilityBlueprint> Capabilities { get; init; } = new();
    public string? ContextSchema { get; init; }
}

// Event Vocabulary
public record EventBlueprint
{
    public string EventId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public EventCategory Category { get; init; } = EventCategory.System;
    public bool IsTerminal { get; init; }
    public string? PayloadSchema { get; init; } // Added: JSON Schema for validation
}

// Law
public record StateMachineBlueprint
{
    public string EntityType { get; init; } = string.Empty;
    public string InitialState { get; init; } = string.Empty;
    public List<string> States { get; init; } = new();
    public List<TransitionBlueprint> Transitions { get; init; } = new();
}

public record TransitionBlueprint
{
    public string FromState { get; init; } = string.Empty;
    public string ToState { get; init; } = string.Empty;
    public string EventId { get; init; } = string.Empty;
    public string? Condition { get; init; }
    public Dictionary<string, string>? Constraints { get; init; }
}

// Orchestration
public record WorkflowBlueprint
{
    public string StartStepId { get; init; } = string.Empty; // Added: Explicit entry point for WORK
    public List<StepBlueprint> Steps { get; init; } = new();
}

public record StepBlueprint
{
    public string StepId { get; init; } = string.Empty;
    public string StepType { get; init; } = "Command"; // Enum mapped to string for blueprint
    public string? DecisionProvider { get; init; } // Optional provider name for pluggable decision evaluation
    public SubWorkflowReferenceBlueprint? SubWorkflow { get; init; } // Optional child workflow reference for SubWorkflow steps
    public Dictionary<string, string> NextSteps { get; init; } = new();
    public List<string> RequiredRoles { get; init; } = new();
    public List<string>? AllowedRoles { get; init; }
    
    // For Decision steps: Condition -> NextStepId
    public Dictionary<string, string> Conditions { get; init; } = new();

    // For Parallel Fork/Join steps
    public List<string> Branches { get; init; } = new(); // Target steps to execute concurrently
    public string JoinPolicy { get; init; } = "WaitAll"; // "WaitAll" or "WaitAny"
    public List<string> InboundSteps { get; init; } = new(); // Steps that must converge at this Join

    // Declarative Step SLA & Boundary Timer
    public StepSlaBlueprint? Sla { get; set; }

    // Declarative Lifecycle Actions (Pre/Post Event & Step Hooks, Saga Rollback)
    public List<StepActionBlueprint> OnEntry { get; set; } = new();
    public List<StepActionBlueprint> OnExit { get; set; } = new();
    public List<StepActionBlueprint> OnFailure { get; set; } = new();
}

public record StepActionBlueprint
{
    public string ActionType { get; init; } = "Notification"; // "Notification", "Webhook", "PublishEvent", "InvokeCapability"
    public string? Target { get; init; } // Role, User ID, Event name, or capability name (legacy fallback)
    public string? Capability { get; init; } // Named capability for InvokeCapability actions
    public string? Url { get; init; } // Webhook URL
    public string? Method { get; init; } = "POST";
    public string? Template { get; init; } // Template identifier or message text
    public Dictionary<string, string>? PayloadMapping { get; init; } // Key -> Expression
    public string? Condition { get; init; } // Optional execution guard
    public Dictionary<string, string>? Headers { get; init; } // Custom HTTP request headers
    public bool SignPayload { get; init; } = true; // Attach HMAC-SHA256 signature
    public string? SecretName { get; init; } // Optional secret name/key
}

public record StepSlaBlueprint
{
    public string Duration { get; init; } = string.Empty;
    public string TimeoutEvent { get; init; } = string.Empty;
    public string? EscalationStepId { get; init; }
    public string? EscalationRole { get; init; }
    public bool IsInterrupting { get; init; } = true;
    public List<StepReminderBlueprint> Reminders { get; set; } = new();
}

public record StepReminderBlueprint
{
    public string Duration { get; init; } = string.Empty;
    public string TriggerEvent { get; init; } = string.Empty;
}

public record SubWorkflowReferenceBlueprint
{
    public Guid? WorkflowDefinitionId { get; init; }
    public Guid? WorkflowClassId { get; init; }
    public string? WorkflowName { get; init; }
    public int? Version { get; init; }
    public Dictionary<string, string> InputMapping { get; init; } = new();
    public Dictionary<string, string> OutputMapping { get; init; } = new();
}

// Governance Declarations
public record RoleBlueprint
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> GrantedCapabilities { get; init; } = new();
}

public record CapabilityBlueprint
{
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
