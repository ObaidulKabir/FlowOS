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
}

// Event Vocabulary
public record EventBlueprint
{
    public string EventId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public EventCategory Category { get; init; } = EventCategory.System;
    public bool IsTerminal { get; init; }
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
    public Dictionary<string, string> NextSteps { get; init; } = new();
    public List<string> RequiredRoles { get; init; } = new();
    
    // For Decision steps: Condition -> NextStepId
    public Dictionary<string, string> Conditions { get; init; } = new();
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
