using FlowOS.Domain.ValueObjects;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.DTOs;

public sealed record WorkflowContextSimulationEventRequest(
    string EventType,
    object? Payload = null,
    IReadOnlyList<string>? Roles = null);

public sealed record WorkflowContextSimulationRequest(
    Guid? ContextBindingId = null,
    string? ContextType = null,
    string Revision = "draft",
    object? InitialPayload = null,
    IReadOnlyList<string>? Roles = null,
    IReadOnlyList<WorkflowContextSimulationEventRequest>? Events = null,
    int MaxSteps = 25,
    bool AutoAdvanceTimers = false);

public sealed record WorkflowContextSimulationProjectionItemDto(
    string CanonicalField,
    string? SourcePath,
    object? Value,
    string Origin,
    bool WasResolved);

public sealed record WorkflowContextSimulationActionDto(
    string StepId,
    string Phase,
    string ActionType,
    string? Target,
    string? Capability,
    string? Condition);

public sealed record WorkflowContextSimulationPendingWorkDto(
    string Kind,
    string StepId,
    string? TriggerEvent,
    string Description);

public sealed record WorkflowContextSimulationTraceDto(
    int Index,
    string EventType,
    string? CanonicalEventType,
    IReadOnlyList<string> Roles,
    string FromStepId,
    string ToStepId,
    IReadOnlyList<string> ActiveStepIds,
    string FromState,
    string ToState,
    bool IsAllowed,
    string Outcome,
    string? Reason,
    IReadOnlyDictionary<string, object?> SourcePayload,
    IReadOnlyDictionary<string, object?> CanonicalDelta,
    IReadOnlyDictionary<string, object?> ContextBefore,
    IReadOnlyDictionary<string, object?> ContextAfter,
    IReadOnlyList<WorkflowContextSimulationActionDto> PlannedActions,
    IReadOnlyList<WorkflowContextSimulationPendingWorkDto> PendingWork);

public sealed record WorkflowContextSimulationWorkflowGraphDto(
    string Name,
    int Version,
    string StartStepId,
    IReadOnlyList<WorkflowStepDefinition> Steps);

public sealed record WorkflowContextSimulationStateMachineGraphDto(
    string EntityType,
    int Version,
    string InitialState,
    IReadOnlyCollection<string> States,
    IReadOnlyList<StateTransition> Transitions);

public sealed record WorkflowContextSimulationGraphDto(
    WorkflowContextSimulationWorkflowGraphDto Workflow,
    WorkflowContextSimulationStateMachineGraphDto StateMachine);

public sealed record WorkflowContextSimulationResultDto(
    Guid ContextBindingId,
    string ContextType,
    string BindingName,
    string RevisionKind,
    Guid ContextBindingRevisionId,
    int Revision,
    Guid SourceWorkflowClassId,
    string SourceWorkflowClassVersion,
    bool IsPersistedRuntime,
    bool SideEffectsSuppressed,
    string Status,
    string InitialStepId,
    string CurrentStepId,
    IReadOnlyList<string> ActiveStepIds,
    string InitialState,
    string CurrentState,
    IReadOnlyList<string> AvailableRoles,
    IReadOnlyDictionary<string, string> EventAliases,
    IReadOnlyList<WorkflowContextSimulationProjectionItemDto> InitialProjection,
    IReadOnlyDictionary<string, object?> InitialCanonicalContext,
    IReadOnlyDictionary<string, object?> FinalCanonicalContext,
    IReadOnlyList<WorkflowContextSimulationTraceDto> Trace,
    IReadOnlyList<WorkflowContextSimulationPendingWorkDto> PendingWork,
    WorkflowContextSimulationGraphDto Graph);
