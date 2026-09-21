using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using System.Diagnostics;

namespace FlowOS.Application.Services;

public sealed class AgentTaskCoordinator : IAgentTaskCoordinator
{
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);

    private readonly IAgentTaskQueue _queue;
    private readonly IAgentTaskRunner _runner;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentExecutionHistoryStore _executionHistory;
    private readonly TimeProvider _timeProvider;

    public AgentTaskCoordinator(
        IAgentTaskQueue queue,
        IAgentTaskRunner runner,
        IUnitOfWork unitOfWork,
        IAgentExecutionHistoryStore executionHistory,
        TimeProvider? timeProvider = null)
    {
        _queue = queue;
        _runner = runner;
        _unitOfWork = unitOfWork;
        _executionHistory = executionHistory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AgentTaskProcessingResult> ProcessClaimAsync(
        AgentTaskClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        AgentExecutionRecord? priorExecution;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            priorExecution = await _executionHistory.GetLatestForJobAsync(
                claim.TenantId,
                claim.JobId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryReleaseForRetryAsync(
                claim,
                "Agent task execution was cancelled.",
                UtcNow(),
                CancellationToken.None);
            throw;
        }

        if (priorExecution?.Status == AgentExecutionStatus.Succeeded)
        {
            var reconstructed = ReconstructResult(priorExecution);
            var completed = await _queue.CompleteAsync(
                claim.JobId,
                claim.Claimant,
                CancellationToken.None);
            var priorStatus = completed
                ? AgentTaskJobStatus.Completed
                : (await _queue.GetAsync(claim.JobId, CancellationToken.None))?.Status
                    ?? AgentTaskJobStatus.Claimed;
            if (priorStatus == AgentTaskJobStatus.Completed &&
                reconstructed.AutoCommitted)
            {
                await EnqueueCurrentStepAsync(
                    claim.TenantId,
                    claim.WorkflowInstanceId,
                    AgentTaskSource.Reconciliation,
                    CancellationToken.None);
            }

            return new AgentTaskProcessingResult(
                claim.JobId,
                priorStatus,
                reconstructed,
                "Reused the durable terminal execution.");
        }
        if (priorExecution?.Status == AgentExecutionStatus.Failed &&
            !IsTransientFailure(priorExecution.FailureCode))
        {
            var deadLettered = await _queue.DeadLetterAsync(
                claim.JobId,
                claim.Claimant,
                priorExecution.SanitizedFailure ?? "Agent execution failed.",
                CancellationToken.None);
            var priorStatus = await ResolveMutationStatusAsync(
                claim.JobId,
                deadLettered,
                AgentTaskJobStatus.DeadLettered);
            return new AgentTaskProcessingResult(
                claim.JobId,
                priorStatus,
                ReconstructResult(priorExecution),
                priorExecution.SanitizedFailure);
        }

        var executionContext = new AgentTaskExecutionContext(
            claim.JobId,
            claim.Claimant,
            claim.Attempt,
            claim.StepId);

        AgentTaskRunResult run;
        try
        {
            run = claim.AllowAutoCommit
                ? await _runner.TryRunForCurrentStepAsync(
                    claim.TenantId,
                    claim.WorkflowInstanceId,
                    claim.RequestedAgentId,
                    cancellationToken,
                    executionContext)
                : await _runner.SuggestAsync(
                    claim.TenantId,
                    claim.WorkflowInstanceId,
                    claim.RequestedAgentId,
                    claim.Objective,
                    cancellationToken,
                    executionContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryReleaseForRetryAsync(
                claim,
                "Agent task execution was cancelled.",
                UtcNow(),
                CancellationToken.None);
            throw;
        }
        catch
        {
            var retryStatus = await TryReleaseForRetryAsync(
                claim,
                "Agent task execution failed.",
                RetryDueAt(claim.Attempt),
                CancellationToken.None);
            return new AgentTaskProcessingResult(
                claim.JobId,
                retryStatus ?? AgentTaskJobStatus.Claimed,
                null,
                "Agent task execution failed.");
        }

        AgentTaskJobStatus status;
        string? message = run.SkipReason ?? run.ParkReason;
        if (!run.Ran)
        {
            if (IsNonWaitingOrStale(run.SkipReason))
            {
                var cancelled = await _queue.CancelAsync(
                    claim.JobId,
                    claim.Claimant,
                    run.SkipReason ?? "Queued agent step is no longer active.",
                    CancellationToken.None);
                status = await ResolveMutationStatusAsync(
                    claim.JobId,
                    cancelled,
                    AgentTaskJobStatus.Cancelled);
            }
            else if (IsTerminalResolutionFailure(run.SkipReason))
            {
                var deadLettered = await _queue.DeadLetterAsync(
                    claim.JobId,
                    claim.Claimant,
                    run.SkipReason ?? "Agent provider configuration is invalid.",
                    CancellationToken.None);
                status = await ResolveMutationStatusAsync(
                    claim.JobId,
                    deadLettered,
                    AgentTaskJobStatus.DeadLettered);
            }
            else
            {
                status = await TryReleaseForRetryAsync(
                    claim,
                    run.SkipReason ?? "Agent execution is temporarily unavailable.",
                    RetryDueAt(claim.Attempt),
                    CancellationToken.None)
                    ?? AgentTaskJobStatus.Claimed;
            }
        }
        else if (run.AgentResult is { Success: false } failure)
        {
            if (IsTransientFailure(failure.FailureCode))
            {
                status = await TryReleaseForRetryAsync(
                    claim,
                    failure.FailureReason ?? "Agent provider is temporarily unavailable.",
                    RetryDueAt(claim.Attempt),
                    CancellationToken.None)
                    ?? AgentTaskJobStatus.Claimed;
            }
            else
            {
                var deadLettered = await _queue.DeadLetterAsync(
                    claim.JobId,
                    claim.Claimant,
                    failure.FailureReason ?? "Agent output was rejected.",
                    CancellationToken.None);
                status = await ResolveMutationStatusAsync(
                    claim.JobId,
                    deadLettered,
                    AgentTaskJobStatus.DeadLettered);
            }
        }
        else
        {
            var completed = await _queue.CompleteAsync(
                claim.JobId,
                claim.Claimant,
                CancellationToken.None);
            status = completed
                ? AgentTaskJobStatus.Completed
                : (await _queue.GetAsync(claim.JobId, CancellationToken.None))?.Status
                    ?? AgentTaskJobStatus.Claimed;

            if (status == AgentTaskJobStatus.Completed && run.AutoCommitted)
            {
                await EnqueueCurrentStepAsync(
                    claim.TenantId,
                    claim.WorkflowInstanceId,
                    AgentTaskSource.Reconciliation,
                    CancellationToken.None);
            }
        }

        return new AgentTaskProcessingResult(
            claim.JobId,
            status,
            run,
            message);
    }

    public async Task<AgentTaskSynchronousResult> EnqueueAndExecuteAsync(
        AgentTaskEnqueueRequest request,
        string claimant,
        TimeSpan waitTimeout,
        TimeSpan pollInterval,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(claimant))
            throw new ArgumentException("Claimant is required.", nameof(claimant));
        if (waitTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(waitTimeout));
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (claimDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(claimDuration));

        var enqueue = await _queue.EnqueueAsync(request, cancellationToken);
        var startedAt = Stopwatch.GetTimestamp();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _queue.ReconcileAsync(enqueue.JobId, cancellationToken);

            var claim = await _queue.ClaimAsync(
                enqueue.JobId,
                claimant,
                claimDuration,
                cancellationToken);
            if (claim != null)
            {
                var processed = await ProcessClaimAsync(claim, cancellationToken);
                if (processed.IsTerminal)
                {
                    return new AgentTaskSynchronousResult(
                        enqueue.JobId,
                        processed.Status,
                        processed.RunResult,
                        Message: processed.Message);
                }
            }

            var snapshot = await _queue.GetAsync(enqueue.JobId, cancellationToken);
            if (snapshot == null)
            {
                return new AgentTaskSynchronousResult(
                    enqueue.JobId,
                    AgentTaskJobStatus.Cancelled,
                    null,
                    Message: "Agent task job was not found.");
            }

            if (snapshot.IsTerminal)
            {
                return new AgentTaskSynchronousResult(
                    snapshot.JobId,
                    snapshot.Status,
                    await ReconstructResultAsync(snapshot, cancellationToken),
                    Message: snapshot.LastError);
            }

            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            if (elapsed >= waitTimeout)
            {
                return new AgentTaskSynchronousResult(
                    snapshot.JobId,
                    snapshot.Status,
                    null,
                    TimedOut: true,
                    Message: "Timed out waiting for the durable agent task.");
            }

            var remaining = waitTimeout - elapsed;
            var waitSlice = remaining < pollInterval ? remaining : pollInterval;
            if (snapshot.Status == AgentTaskJobStatus.Claimed &&
                !string.Equals(snapshot.Claimant, claimant, StringComparison.Ordinal))
            {
                await _queue.WaitForTerminalAsync(
                    snapshot.JobId,
                    waitSlice,
                    waitSlice,
                    cancellationToken);
            }
            else
            {
                await Task.Delay(waitSlice, cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<AgentTaskEnqueueResult>> EnqueueCurrentStepAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        AgentTaskSource source,
        CancellationToken cancellationToken = default)
    {
        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsNoTrackingAsync(
                workflowInstanceId,
                tenantId,
                cancellationToken);
        if (instance == null)
            return Array.Empty<AgentTaskEnqueueResult>();

        var definition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsNoTrackingAsync(
                instance.WorkflowDefinitionId,
                cancellationToken);
        if (definition == null)
            return Array.Empty<AgentTaskEnqueueResult>();

        var results = new List<AgentTaskEnqueueResult>();
        foreach (var pending in AgentTaskScheduling.ForCurrentWaitingStep(
                     instance,
                     definition,
                     source))
        {
            results.Add(await _queue.EnqueueAsync(pending, cancellationToken));
        }

        return results;
    }

    private async Task<AgentTaskRunResult?> ReconstructResultAsync(
        AgentTaskJobSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var record = await _executionHistory.GetLatestForJobAsync(
            snapshot.TenantId,
            snapshot.JobId,
            cancellationToken);
        if (record == null)
            return null;

        return ReconstructResult(record);
    }

    private static AgentTaskRunResult ReconstructResult(
        AgentExecutionRecord record)
    {
        AgentResult result;
        if (record.Success == false)
        {
            result = AgentResult.Failure(
                record.FailureCode ?? AgentFailureCodes.ProviderUnavailable,
                record.SanitizedFailure ?? "Agent execution failed.",
                Telemetry(record));
        }
        else if (!string.IsNullOrWhiteSpace(record.SuggestedEvent))
        {
            result = AgentResult.WithActions(
                "Reconstructed from the durable agent execution record.",
                [
                    new SuggestedAction(
                        record.SuggestedEvent,
                        "Durable execution result.",
                        record.Confidence ?? 0)
                ]);
            result.Telemetry = Telemetry(record);
        }
        else
        {
            result = AgentResult.FromInsight(
                "Reconstructed from the durable agent execution record.");
            result.Telemetry = Telemetry(record);
        }

        var actorId = record.Actor.StartsWith("Agent:", StringComparison.OrdinalIgnoreCase)
            ? record.Actor["Agent:".Length..]
            : record.Actor;
        return new AgentTaskRunResult(
            true,
            record.WasCommitted,
            null,
            record.ParkReason,
            actorId,
            result,
            null,
            record.RuntimeIdentifier,
            record.ProviderAlias,
            record.ProviderName,
            record.Model,
            record.ExecutionId,
            record.JobId);
    }

    private async Task<AgentTaskJobStatus?> TryReleaseForRetryAsync(
        AgentTaskClaim claim,
        string error,
        DateTime dueAtUtc,
        CancellationToken cancellationToken)
    {
        var status = await _queue.RetryAsync(
            claim.JobId,
            claim.Claimant,
            error,
            dueAtUtc,
            cancellationToken);
        return status ?? (await _queue.GetAsync(
            claim.JobId,
            CancellationToken.None))?.Status;
    }

    private async Task<AgentTaskJobStatus> ResolveMutationStatusAsync(
        Guid jobId,
        bool mutationApplied,
        AgentTaskJobStatus appliedStatus)
    {
        if (mutationApplied)
            return appliedStatus;

        return (await _queue.GetAsync(jobId, CancellationToken.None))?.Status
            ?? AgentTaskJobStatus.Claimed;
    }

    private DateTime RetryDueAt(int attempt)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 10);
        var delay = TimeSpan.FromSeconds(Math.Pow(2, exponent));
        if (delay > MaximumRetryDelay)
            delay = MaximumRetryDelay;
        return UtcNow().Add(delay);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static bool IsTransientFailure(string? failureCode) =>
        failureCode is AgentFailureCodes.ProviderRateLimit
            or AgentFailureCodes.ProviderTimeout
            or AgentFailureCodes.ProviderUnavailable;

    private static bool IsNonWaitingOrStale(string? reason) =>
        reason?.Contains("no longer waiting", StringComparison.OrdinalIgnoreCase) == true ||
        reason?.Contains("not waiting", StringComparison.OrdinalIgnoreCase) == true ||
        reason?.Contains("not a waiting", StringComparison.OrdinalIgnoreCase) == true ||
        reason?.Contains("not running", StringComparison.OrdinalIgnoreCase) == true ||
        reason?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
        reason?.Contains("packet could not be built", StringComparison.OrdinalIgnoreCase) == true ||
        reason?.StartsWith("Step actor is", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsTerminalResolutionFailure(string? reason) =>
        reason?.Contains("authentication", StringComparison.OrdinalIgnoreCase) == true ||
        reason?.Contains("configuration", StringComparison.OrdinalIgnoreCase) == true ||
        reason?.StartsWith("No hosted agent is registered", StringComparison.Ordinal) == true ||
        reason?.StartsWith("MCP-PLAN-REQUIRED", StringComparison.Ordinal) == true;

    private static AgentTelemetry Telemetry(AgentExecutionRecord record) =>
        new(
            record.HttpStatusCode,
            InputTokens: record.InputTokens,
            OutputTokens: record.OutputTokens);
}
