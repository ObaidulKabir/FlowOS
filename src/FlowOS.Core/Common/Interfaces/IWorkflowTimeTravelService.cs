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
    string Summary
);

public record WorkflowTimeTravelReplayDto(
    Guid WorkflowInstanceId,
    string WorkflowClassName,
    int WorkflowVersion,
    string Status,
    int TotalSteps,
    List<WorkflowTimeTravelSnapshotDto> Snapshots
);

public record WorkflowForkSimulationRequest(
    int TargetStepIndex,
    string AlternativeEvent,
    object? AlternativePayload = null
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
    List<string> ProjectedActions
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
        CancellationToken cancellationToken = default);

    Task<WorkflowCompensationPathDto?> PlanCompensationPathAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? failedStepId = null,
        CancellationToken cancellationToken = default);
}
