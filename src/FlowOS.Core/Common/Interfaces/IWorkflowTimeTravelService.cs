using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Core.Common.Interfaces;

public record WorkflowTimeTravelSnapshotDto(
    int StepIndex,
    DateTime Timestamp,
    Guid EventId,
    string EventType,
    string FromStepId,
    string ToStepId,
    List<string> ActiveStepIds,
    string FromState,
    string ToState,
    string? ActorId,
    Dictionary<string, object?> Variables,
    List<WorkflowActionExecutionLogDto> ActionLogs,
    string Summary,
    string? ContextualEventType = null,
    string? CanonicalEventType = null,
    Guid? ContextBindingRevisionId = null
);

public record WorkflowTimeTravelReplayDto(
    Guid WorkflowInstanceId,
    string WorkflowClassName,
    int WorkflowVersion,
    string Status,
    int TotalSteps,
    List<WorkflowTimeTravelSnapshotDto> Snapshots,
    Guid? ContextBindingId = null,
    Guid? ContextBindingRevisionId = null,
    string? ContextType = null,
    string? SourceSystem = null,
    string? ExternalEntityId = null
);

public record WorkflowForkSimulationRequest(
    int TargetStepIndex,
    string AlternativeEvent,
    object? AlternativePayload = null,
    IReadOnlyList<string>? SimulatedRoles = null
);

public record WorkflowForkSimulationResultDto(
    int ForkFromStepIndex,
    string BaseStepId,
    string BaseState,
    string AlternativeEvent,
    string ProjectedStepId,
    string ProjectedState,
    bool IsAllowed,
    string? Reason,
    List<string> ProjectedActions,
    IReadOnlyList<string>? SimulatedRoles = null,
    string? ContextualEventType = null,
    string? CanonicalEventType = null,
    Guid? ContextBindingId = null,
    Guid? ContextBindingRevisionId = null,
    string? ContextType = null,
    Dictionary<string, object?>? ProjectedCanonicalContext = null
);

public record WorkflowCompensationPathDto(
    Guid WorkflowInstanceId,
    string FailedStepId,
    List<string> ExecutedStepIds,
    bool IsFullyCompensable,
    List<CompensationStepPlanDto> OrderedCompensations,
    List<string> BlockedSteps);

public interface IWorkflowTimeTravelService
{
    Task<WorkflowTimeTravelReplayDto?> GetReplayTimelineAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        CancellationToken cancellationToken = default);

    Task<WorkflowForkSimulationResultDto> SimulateForkAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        int targetStepIndex,
        string alternativeEvent,
        object? alternativePayload = null,
        IReadOnlyList<string>? simulatedRoles = null,
        CancellationToken cancellationToken = default);

    Task<WorkflowCompensationPathDto?> PlanCompensationPathAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? failedStepId = null,
        CancellationToken cancellationToken = default);
}
