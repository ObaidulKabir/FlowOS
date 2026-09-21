using FlowOS.Domain.Enums;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public sealed record AgentTaskEnqueueRequest(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string StepId,
    string RequestedAgentId,
    string? Objective,
    bool AllowAutoCommit,
    bool RequireAgentActor,
    AgentTaskSource Source,
    DateTime? DueAtUtc = null,
    int MaxAttempts = 5,
    string? ActiveKey = null);

public sealed record AgentTaskEnqueueResult(
    Guid JobId,
    bool Created,
    AgentTaskJobStatus Status,
    string ActiveKey);

public sealed record AgentTaskClaim(
    Guid JobId,
    Guid TenantId,
    Guid WorkflowInstanceId,
    string StepId,
    string RequestedAgentId,
    string? Objective,
    bool AllowAutoCommit,
    bool RequireAgentActor,
    AgentTaskSource Source,
    int Attempt,
    int MaxAttempts,
    DateTime RequestedAtUtc,
    DateTime DueAtUtc,
    DateTime ClaimedAtUtc,
    DateTime ClaimExpiresAtUtc,
    string Claimant);

public sealed record AgentTaskJobSnapshot(
    Guid JobId,
    Guid TenantId,
    Guid WorkflowInstanceId,
    string StepId,
    string RequestedAgentId,
    string? Objective,
    bool AllowAutoCommit,
    bool RequireAgentActor,
    AgentTaskSource Source,
    AgentTaskJobStatus Status,
    int Attempts,
    int MaxAttempts,
    DateTime RequestedAtUtc,
    DateTime DueAtUtc,
    DateTime? ClaimedAtUtc,
    DateTime? ClaimExpiresAtUtc,
    DateTime? CompletedAtUtc,
    string? Claimant,
    string? LastError)
{
    public bool IsTerminal =>
        Status is AgentTaskJobStatus.Completed
            or AgentTaskJobStatus.DeadLettered
            or AgentTaskJobStatus.Cancelled;
}

public interface IAgentTaskQueue
{
    /// <summary>
    /// Adds a job to the current persistence context without saving it. This is
    /// used by workflow command handlers so the state mutation, event rows, and
    /// agent job are committed by one SaveChanges call.
    /// </summary>
    Task<AgentTaskEnqueueResult> StageAsync(
        AgentTaskEnqueueRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentTaskEnqueueResult> EnqueueAsync(
        AgentTaskEnqueueRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentTaskClaim?> ClaimAsync(
        Guid jobId,
        string claimant,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentTaskClaim>> ClaimBatchAsync(
        string claimant,
        int batchSize,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(
        Guid jobId,
        string claimant,
        CancellationToken cancellationToken = default);

    Task<AgentTaskJobStatus?> RetryAsync(
        Guid jobId,
        string claimant,
        string sanitizedError,
        DateTime dueAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> DeadLetterAsync(
        Guid jobId,
        string claimant,
        string sanitizedError,
        CancellationToken cancellationToken = default);

    Task<bool> CancelAsync(
        Guid jobId,
        string claimant,
        string? sanitizedReason = null,
        CancellationToken cancellationToken = default);

    Task<AgentTaskJobSnapshot?> GetAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<AgentTaskJobSnapshot?> WaitForTerminalAsync(
        Guid jobId,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default);

    Task<AgentTaskJobStatus?> ReconcileAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<int> ReconcileAsync(CancellationToken cancellationToken = default);
}
