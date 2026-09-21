using System.Data;
using System.Diagnostics;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public sealed class AgentTaskQueue : IAgentTaskQueue
{
    private const int ReconcileBatchSize = 500;

    private readonly FlowOSDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public AgentTaskQueue(FlowOSDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AgentTaskEnqueueResult> StageAsync(
        AgentTaskEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var activeKey = ResolveActiveKey(request);
        var tracked = _dbContext.AgentTaskJobs.Local
            .FirstOrDefault(x => string.Equals(x.ActiveKey, activeKey, StringComparison.Ordinal));
        if (tracked != null)
        {
            return new AgentTaskEnqueueResult(
                tracked.Id,
                false,
                tracked.Status,
                activeKey);
        }

        var existing = await _dbContext.AgentTaskJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActiveKey == activeKey, cancellationToken);
        if (existing != null)
        {
            return new AgentTaskEnqueueResult(
                existing.Id,
                false,
                existing.Status,
                activeKey);
        }

        var job = CreateJob(request, activeKey);
        _dbContext.AgentTaskJobs.Add(job);
        return new AgentTaskEnqueueResult(job.Id, true, job.Status, activeKey);
    }

    public async Task<AgentTaskEnqueueResult> EnqueueAsync(
        AgentTaskEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsNpgsql)
        {
            var activeKey = ResolveActiveKey(request);
            return await EnqueuePostgresAsync(
                CreateJob(request, activeKey),
                cancellationToken);
        }

        var staged = await StageAsync(request, cancellationToken);
        if (staged.Created)
            await _dbContext.SaveChangesAsync(cancellationToken);
        return staged;
    }

    public async Task<AgentTaskClaim?> ClaimAsync(
        Guid jobId,
        string claimant,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateClaimArguments(claimant, claimDuration);
        if (jobId == Guid.Empty)
            return null;

        var normalizedClaimant = claimant.Trim();
        var now = UtcNow();
        var expiresAt = now.Add(claimDuration);

        if (IsNpgsql)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var job = await _dbContext.AgentTaskJobs
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "AgentTaskJobs"
                    WHERE "Id" = {jobId}
                    FOR UPDATE SKIP LOCKED
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (job == null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var claim = TryClaim(job, normalizedClaimant, now, expiresAt);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claim;
        }

        return await WithLockedJobAsync<AgentTaskClaim?>(
            jobId,
            job => TryClaim(job, normalizedClaimant, now, expiresAt),
            null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<AgentTaskClaim>> ClaimBatchAsync(
        string claimant,
        int batchSize,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateClaimArguments(claimant, claimDuration);
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be between 1 and 1000.");

        var normalizedClaimant = claimant.Trim();
        var now = UtcNow();
        var expiresAt = now.Add(claimDuration);

        if (IsNpgsql)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var pending = AgentTaskJobStatus.Pending.ToString();
            var retry = AgentTaskJobStatus.RetryScheduled.ToString();
            var jobs = await _dbContext.AgentTaskJobs
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "AgentTaskJobs"
                    WHERE "Status" IN ({pending}, {retry})
                      AND "DueAtUtc" <= {now}
                    ORDER BY "DueAtUtc", "RequestedAtUtc", "Id"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            foreach (var job in jobs)
                job.Claim(normalizedClaimant, now, expiresAt);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return jobs.Select(ToClaim).ToArray();
        }

        var candidates = await _dbContext.AgentTaskJobs
            .Where(x =>
                (x.Status == AgentTaskJobStatus.Pending ||
                 x.Status == AgentTaskJobStatus.RetryScheduled) &&
                x.DueAtUtc <= now)
            .OrderBy(x => x.DueAtUtc)
            .ThenBy(x => x.RequestedAtUtc)
            .ThenBy(x => x.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var job in candidates)
            job.Claim(normalizedClaimant, now, expiresAt);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return candidates.Select(ToClaim).ToArray();
    }

    public Task<bool> CompleteAsync(
        Guid jobId,
        string claimant,
        CancellationToken cancellationToken = default) =>
        WithLockedJobAsync(
            jobId,
            job =>
            {
                var normalizedClaimant = claimant?.Trim();
                if (job.Status == AgentTaskJobStatus.Completed)
                    return true;
                if (job.IsTerminal ||
                    job.Status != AgentTaskJobStatus.Claimed ||
                    string.IsNullOrWhiteSpace(normalizedClaimant) ||
                    !string.Equals(job.Claimant, normalizedClaimant, StringComparison.Ordinal))
                {
                    return false;
                }

                job.Complete(normalizedClaimant, UtcNow());
                return true;
            },
            false,
            cancellationToken);

    public Task<AgentTaskJobStatus?> RetryAsync(
        Guid jobId,
        string claimant,
        string sanitizedError,
        DateTime dueAtUtc,
        CancellationToken cancellationToken = default) =>
        WithLockedJobAsync<AgentTaskJobStatus?>(
            jobId,
            job =>
            {
                var normalizedClaimant = claimant?.Trim();
                if (job.IsTerminal)
                    return job.Status;
                if (job.Status != AgentTaskJobStatus.Claimed ||
                    string.IsNullOrWhiteSpace(normalizedClaimant) ||
                    !string.Equals(job.Claimant, normalizedClaimant, StringComparison.Ordinal))
                {
                    return null;
                }

                job.Retry(
                    normalizedClaimant,
                    SanitizeError(sanitizedError) ?? "Agent task retry requested.",
                    dueAtUtc,
                    UtcNow());
                return job.Status;
            },
            null,
            cancellationToken);

    public Task<bool> DeadLetterAsync(
        Guid jobId,
        string claimant,
        string sanitizedError,
        CancellationToken cancellationToken = default) =>
        WithLockedJobAsync(
            jobId,
            job =>
            {
                var normalizedClaimant = claimant?.Trim();
                if (job.Status == AgentTaskJobStatus.DeadLettered)
                    return true;
                if (job.IsTerminal ||
                    job.Status != AgentTaskJobStatus.Claimed ||
                    string.IsNullOrWhiteSpace(normalizedClaimant) ||
                    !string.Equals(job.Claimant, normalizedClaimant, StringComparison.Ordinal))
                {
                    return false;
                }

                job.DeadLetter(
                    normalizedClaimant,
                    SanitizeError(sanitizedError) ?? "Agent task was dead-lettered.",
                    UtcNow());
                return true;
            },
            false,
            cancellationToken);

    public Task<bool> CancelAsync(
        Guid jobId,
        string claimant,
        string? sanitizedReason = null,
        CancellationToken cancellationToken = default) =>
        WithLockedJobAsync(
            jobId,
            job =>
            {
                var normalizedClaimant = claimant?.Trim();
                if (job.Status == AgentTaskJobStatus.Cancelled)
                    return true;
                if (job.IsTerminal ||
                    job.Status != AgentTaskJobStatus.Claimed ||
                    string.IsNullOrWhiteSpace(normalizedClaimant) ||
                    !string.Equals(job.Claimant, normalizedClaimant, StringComparison.Ordinal))
                {
                    return false;
                }

                job.Cancel(SanitizeError(sanitizedReason), UtcNow());
                return true;
            },
            false,
            cancellationToken);

    public Task<AgentTaskJobSnapshot?> GetAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        if (jobId == Guid.Empty)
            return Task.FromResult<AgentTaskJobSnapshot?>(null);

        return GetSnapshotAsync(jobId, cancellationToken);
    }

    public async Task<AgentTaskJobSnapshot?> WaitForTerminalAsync(
        Guid jobId,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));

        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await GetSnapshotAsync(jobId, cancellationToken);
            if (snapshot == null || snapshot.IsTerminal)
                return snapshot;

            if (Stopwatch.GetElapsedTime(startedAt) >= timeout)
                return snapshot;

            var remaining = timeout - Stopwatch.GetElapsedTime(startedAt);
            await Task.Delay(
                remaining < pollInterval ? remaining : pollInterval,
                cancellationToken);
        }
    }

    public async Task<AgentTaskJobStatus?> ReconcileAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        if (jobId == Guid.Empty)
            return null;

        var now = UtcNow();
        return await WithLockedJobAsync<AgentTaskJobStatus?>(
            jobId,
            job =>
            {
                job.ReconcileExpiredClaim(
                    now,
                    "Agent task claim expired before completion.");
                return job.Status;
            },
            null,
            cancellationToken);
    }

    public async Task<int> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        List<AgentTaskJob> expired;

        if (IsNpgsql)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var claimed = AgentTaskJobStatus.Claimed.ToString();
            expired = await _dbContext.AgentTaskJobs
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "AgentTaskJobs"
                    WHERE "Status" = {claimed}
                      AND "ClaimExpiresAtUtc" <= {now}
                    ORDER BY "ClaimExpiresAtUtc", "Id"
                    LIMIT {ReconcileBatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            var reconciled = Reconcile(expired, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return reconciled;
        }

        expired = await _dbContext.AgentTaskJobs
            .Where(x =>
                x.Status == AgentTaskJobStatus.Claimed &&
                x.ClaimExpiresAtUtc <= now)
            .OrderBy(x => x.ClaimExpiresAtUtc)
            .ThenBy(x => x.Id)
            .Take(ReconcileBatchSize)
            .ToListAsync(cancellationToken);
        var count = Reconcile(expired, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return count;
    }

    private async Task<AgentTaskEnqueueResult> EnqueuePostgresAsync(
        AgentTaskJob job,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var source = job.Source.ToString();
        var status = job.Status.ToString();
        var inserted = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskJobs"
                ("Id", "TenantId", "WorkflowInstanceId", "StepId", "RequestedAgentId",
                 "Objective", "AllowAutoCommit", "RequireAgentActor", "Source", "Status",
                 "ActiveKey", "Attempts", "MaxAttempts", "RequestedAtUtc", "DueAtUtc")
            VALUES
                ({job.Id}, {job.TenantId}, {job.WorkflowInstanceId}, {job.StepId}, {job.RequestedAgentId},
                 {job.Objective}, {job.AllowAutoCommit}, {job.RequireAgentActor}, {source}, {status},
                 {job.ActiveKey}, {job.Attempts}, {job.MaxAttempts}, {job.RequestedAtUtc}, {job.DueAtUtc})
            ON CONFLICT ("ActiveKey") WHERE "ActiveKey" IS NOT NULL DO NOTHING
            """, cancellationToken);

        var persisted = await _dbContext.AgentTaskJobs
            .AsNoTracking()
            .SingleAsync(x => x.ActiveKey == job.ActiveKey, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AgentTaskEnqueueResult(
            persisted.Id,
            inserted == 1,
            persisted.Status,
            persisted.ActiveKey!);
    }

    private async Task<TResult> WithLockedJobAsync<TResult>(
        Guid jobId,
        Func<AgentTaskJob, TResult> mutation,
        TResult missingResult,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
            return missingResult;

        if (IsNpgsql)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var job = await _dbContext.AgentTaskJobs
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "AgentTaskJobs"
                    WHERE "Id" = {jobId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (job == null)
            {
                await transaction.CommitAsync(cancellationToken);
                return missingResult;
            }

            var result = mutation(job);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        var fallbackJob = await _dbContext.AgentTaskJobs
            .SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (fallbackJob == null)
            return missingResult;

        var fallbackResult = mutation(fallbackJob);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return fallbackResult;
    }

    private async Task<AgentTaskJobSnapshot?> GetSnapshotAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await _dbContext.AgentTaskJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        return job == null ? null : ToSnapshot(job);
    }

    private AgentTaskJob CreateJob(
        AgentTaskEnqueueRequest request,
        string activeKey)
    {
        var now = UtcNow();
        return new AgentTaskJob(
            request.TenantId,
            request.WorkflowInstanceId,
            request.StepId,
            request.RequestedAgentId,
            request.Objective,
            request.AllowAutoCommit,
            request.RequireAgentActor,
            request.Source,
            activeKey,
            now,
            request.DueAtUtc ?? now,
            request.MaxAttempts);
    }

    private static string ResolveActiveKey(AgentTaskEnqueueRequest request) =>
        string.IsNullOrWhiteSpace(request.ActiveKey)
            ? AgentTaskJob.CreateActiveKey(
                request.TenantId,
                request.WorkflowInstanceId,
                request.StepId)
            : request.ActiveKey.Trim();

    private static void ValidateClaimArguments(
        string claimant,
        TimeSpan claimDuration)
    {
        if (string.IsNullOrWhiteSpace(claimant))
            throw new ArgumentException("Claimant is required.", nameof(claimant));
        if (claimDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(claimDuration));
    }

    private static int Reconcile(IEnumerable<AgentTaskJob> jobs, DateTime nowUtc)
    {
        var count = 0;
        foreach (var job in jobs)
        {
            if (job.ReconcileExpiredClaim(nowUtc, "Agent task claim expired before completion."))
                count++;
        }

        return count;
    }

    private static AgentTaskClaim? TryClaim(
        AgentTaskJob job,
        string claimant,
        DateTime now,
        DateTime expiresAt)
    {
        if (job.Status == AgentTaskJobStatus.Claimed &&
            job.ClaimExpiresAtUtc <= now)
        {
            job.ReconcileExpiredClaim(
                now,
                "Agent task claim expired before completion.");
        }

        if (job.Status is not (AgentTaskJobStatus.Pending or AgentTaskJobStatus.RetryScheduled) ||
            job.DueAtUtc > now)
        {
            return null;
        }

        job.Claim(claimant, now, expiresAt);
        return ToClaim(job);
    }

    private static AgentTaskClaim ToClaim(AgentTaskJob job) =>
        new(
            job.Id,
            job.TenantId,
            job.WorkflowInstanceId,
            job.StepId,
            job.RequestedAgentId,
            job.Objective,
            job.AllowAutoCommit,
            job.RequireAgentActor,
            job.Source,
            job.Attempts,
            job.MaxAttempts,
            job.RequestedAtUtc,
            job.DueAtUtc,
            job.ClaimedAtUtc!.Value,
            job.ClaimExpiresAtUtc!.Value,
            job.Claimant!);

    private static AgentTaskJobSnapshot ToSnapshot(AgentTaskJob job) =>
        new(
            job.Id,
            job.TenantId,
            job.WorkflowInstanceId,
            job.StepId,
            job.RequestedAgentId,
            job.Objective,
            job.AllowAutoCommit,
            job.RequireAgentActor,
            job.Source,
            job.Status,
            job.Attempts,
            job.MaxAttempts,
            job.RequestedAtUtc,
            job.DueAtUtc,
            job.ClaimedAtUtc,
            job.ClaimExpiresAtUtc,
            job.CompletedAtUtc,
            job.Claimant,
            job.LastError);

    private static string? SanitizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return null;

        var sanitized = new string(
            error
                .Where(character => !char.IsControl(character) || character == ' ')
                .ToArray())
            .Trim();
        if (sanitized.Length == 0)
            return null;
        var sensitiveMarkers = new[]
        {
            "authorization:",
            "bearer ",
            "api_key",
            "apikey",
            "password",
            "secret="
        };
        if (sensitiveMarkers.Any(marker =>
                sanitized.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return "Agent task failed; provider details were redacted.";
        }

        return sanitized.Length <= 1000
            ? sanitized
            : sanitized[..1000];
    }

    private bool IsNpgsql =>
        _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
