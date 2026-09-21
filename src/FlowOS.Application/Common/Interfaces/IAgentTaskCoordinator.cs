using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Enums;

namespace FlowOS.Application.Common.Interfaces;

public sealed record AgentTaskProcessingResult(
    Guid JobId,
    AgentTaskJobStatus Status,
    AgentTaskRunResult? RunResult,
    string? Message = null)
{
    public bool IsTerminal =>
        Status is AgentTaskJobStatus.Completed
            or AgentTaskJobStatus.DeadLettered
            or AgentTaskJobStatus.Cancelled;
}

public sealed record AgentTaskSynchronousResult(
    Guid JobId,
    AgentTaskJobStatus Status,
    AgentTaskRunResult? RunResult,
    bool TimedOut = false,
    string? Message = null);

public interface IAgentTaskCoordinator
{
    Task<AgentTaskProcessingResult> ProcessClaimAsync(
        AgentTaskClaim claim,
        CancellationToken cancellationToken = default);

    Task<AgentTaskSynchronousResult> EnqueueAndExecuteAsync(
        AgentTaskEnqueueRequest request,
        string claimant,
        TimeSpan waitTimeout,
        TimeSpan pollInterval,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentTaskEnqueueResult>> EnqueueCurrentStepAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        AgentTaskSource source,
        CancellationToken cancellationToken = default);
}
