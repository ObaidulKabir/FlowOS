using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Infrastructure;

public sealed class AgentPersistenceTests
{
    [Fact]
    public void AgentTaskJob_Enforces_Terminal_Transitions_And_Clears_Dedupe_Key()
    {
        var now = Utc(2026, 9, 21);
        var job = NewJob(now, maxAttempts: 2);

        job.Claim("worker-1", now, now.AddMinutes(1));
        job.Retry("worker-1", "temporary", now.AddMinutes(2), now.AddSeconds(5));

        Assert.Equal(AgentTaskJobStatus.RetryScheduled, job.Status);
        Assert.NotNull(job.ActiveKey);
        Assert.Equal(1, job.Attempts);

        job.Claim("worker-2", now.AddMinutes(2), now.AddMinutes(3));
        job.Retry("worker-2", "still unavailable", now.AddMinutes(4), now.AddMinutes(2));

        Assert.Equal(AgentTaskJobStatus.DeadLettered, job.Status);
        Assert.Null(job.ActiveKey);
        Assert.Null(job.Claimant);
        Assert.NotNull(job.CompletedAtUtc);
        Assert.Equal(2, job.Attempts);
    }

    [Fact]
    public async Task AgentTaskQueue_Enqueues_Idempotently()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(Utc(2026, 9, 21));
        var queue = new AgentTaskQueue(db, clock);
        var request = NewRequest();

        var first = await queue.EnqueueAsync(request);
        var duplicate = await queue.EnqueueAsync(request);

        Assert.True(first.Created);
        Assert.False(duplicate.Created);
        Assert.Equal(first.JobId, duplicate.JobId);
        Assert.Equal(first.ActiveKey, duplicate.ActiveKey);
        Assert.Equal(1, await db.AgentTaskJobs.CountAsync());
    }

    [Fact]
    public async Task AgentTaskQueue_Claims_Completes_Retries_DeadLetters_And_Reconciles()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(Utc(2026, 9, 21));
        var queue = new AgentTaskQueue(db, clock);

        var completed = await queue.EnqueueAsync(NewRequest(stepId: "complete"));
        var completionClaim = Assert.Single(
            await queue.ClaimBatchAsync("worker-a", 1, TimeSpan.FromMinutes(1)));
        Assert.Equal(completed.JobId, completionClaim.JobId);
        Assert.True(await queue.CompleteAsync(completed.JobId, "worker-a"));

        var retried = await queue.EnqueueAsync(NewRequest(stepId: "retry", maxAttempts: 2));
        var retryClaim = Assert.Single(
            await queue.ClaimBatchAsync("worker-a", 1, TimeSpan.FromMinutes(1)));
        Assert.Equal(retried.JobId, retryClaim.JobId);
        Assert.Equal(
            AgentTaskJobStatus.RetryScheduled,
            await queue.RetryAsync(retried.JobId, "worker-a", "transient", clock.UtcNow));

        var finalClaim = Assert.Single(
            await queue.ClaimBatchAsync("worker-b", 1, TimeSpan.FromMinutes(1)));
        Assert.Equal(retried.JobId, finalClaim.JobId);
        Assert.Equal(
            AgentTaskJobStatus.DeadLettered,
            await queue.RetryAsync(retried.JobId, "worker-b", "terminal", clock.UtcNow));

        var expired = await queue.EnqueueAsync(NewRequest(stepId: "expired", maxAttempts: 2));
        Assert.Single(await queue.ClaimBatchAsync("worker-c", 1, TimeSpan.FromSeconds(30)));
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(1, await queue.ReconcileAsync());
        var expiredJob = await db.AgentTaskJobs.SingleAsync(x => x.Id == expired.JobId);
        Assert.Equal(AgentTaskJobStatus.RetryScheduled, expiredJob.Status);
        Assert.Null(expiredJob.Claimant);
    }

    [Fact]
    public async Task DistributedLease_Expires_And_Can_Be_Reacquired()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(Utc(2026, 9, 21));
        var leases = new DistributedLeaseService(db, clock);

        Assert.NotNull(await leases.TryAcquireAsync("agent-worker", "owner-a", TimeSpan.FromMinutes(1)));
        Assert.Null(await leases.TryAcquireAsync("agent-worker", "owner-b", TimeSpan.FromMinutes(1)));

        clock.Advance(TimeSpan.FromMinutes(2));
        var acquired = await leases.TryAcquireAsync("agent-worker", "owner-b", TimeSpan.FromMinutes(1));

        Assert.NotNull(acquired);
        Assert.Equal("owner-b", acquired!.OwnerId);
        Assert.False(await leases.ReleaseAsync("agent-worker", "owner-a"));
        Assert.True(await leases.ReleaseAsync("agent-worker", "owner-b"));
    }

    [Fact]
    public async Task HostedLlmUsageStore_Reserves_Finalizes_And_Enforces_Quota()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(Utc(2026, 9, 21));
        var usage = new HostedLlmUsageStore(db, clock);
        var tenantId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(clock.UtcNow);

        Assert.True((await usage.TryReserveAsync(tenantId, date, "gpt-test", 2)).Allowed);
        Assert.True((await usage.TryReserveAsync(tenantId, date, "gpt-test", 2)).Allowed);
        Assert.False((await usage.TryReserveAsync(tenantId, date, "gpt-test", 2)).Allowed);

        await usage.FinalizeAsync(tenantId, date, "gpt-test", true, 10, 4);
        var snapshot = await usage.FinalizeAsync(tenantId, date, "gpt-test", false, 3, 1);

        Assert.Equal(0, snapshot.ReservedRequests);
        Assert.Equal(2, snapshot.FinalizedRequests);
        Assert.Equal(1, snapshot.SuccessfulRequests);
        Assert.Equal(1, snapshot.FailedRequests);
        Assert.Equal(13, snapshot.InputTokens);
        Assert.Equal(5, snapshot.OutputTokens);
        Assert.Equal(snapshot, await usage.ReadAsync(tenantId, date, "gpt-test"));
    }

    [Fact]
    public async Task AgentExecutionStore_Records_History_Outcome_And_Override()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(Utc(2026, 9, 21));
        var store = new AgentExecutionStore(db, clock);
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var executionId = await store.StartAsync(new AgentExecutionStartRequest(
            tenantId,
            instanceId,
            "review",
            "Agent:risk",
            AgentExecutionMode.Live,
            JobId: jobId,
            Claimant: "worker-test",
            ProviderAlias: "hosted",
            ProviderName: "openai",
            Model: "gpt-test",
            PromptAlias: "risk-v2",
            RuntimeIdentifier: "flowos",
            RuntimeVersion: "1",
            WorkflowDefinitionId: Guid.NewGuid(),
            WorkflowDefinitionVersion: 3,
            ContextBindingId: Guid.NewGuid(),
            ContextBindingRevisionId: Guid.NewGuid(),
            ContextVersion: 7,
            IdempotencyKey: "agent-exec:test",
            CorrelationId: correlationId));
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(await store.CompleteAsync(
            executionId,
            new AgentExecutionCompletion(
                true,
                InputTokens: 20,
                OutputTokens: 6,
                SuggestedEvent: "Approve",
                Confidence: 0.97,
                WasCommitted: true)));
        Assert.True(await store.RecordObservedOutcomeAsync(executionId, "Approved", true));
        Assert.True(await store.RecordOverrideAsync(
            executionId,
            "User:reviewer",
            "Escalate",
            "Human review superseded the automatic decision."));

        var record = await store.GetAsync(tenantId, executionId);
        Assert.NotNull(record);
        Assert.Equal(AgentExecutionStatus.Succeeded, record!.Status);
        Assert.Equal(AgentExecutionOutcome.Committed, record.DecisionOutcome);
        Assert.Equal(2000, record.DurationMs);
        Assert.True(record.WasOverridden);
        Assert.Equal(correlationId, record.CorrelationId);
        Assert.Equal(jobId, record.JobId);
        Assert.Equal("worker-test", record.Claimant);

        var history = await store.ListAsync(new AgentExecutionHistoryQuery(
            tenantId,
            WorkflowInstanceId: instanceId));
        Assert.Single(history);
        Assert.Equal(executionId, history[0].ExecutionId);
    }

    [Fact]
    public async Task Cleanup_Removes_Only_Expired_Agent_Persistence_Rows()
    {
        await using var db = CreateDb();
        var now = Utc(2026, 9, 21);
        var clock = new MutableTimeProvider(now);
        var old = now.AddDays(-100);

        var oldJob = NewJob(old);
        oldJob.Claim("worker", old, old.AddMinutes(1));
        oldJob.Complete("worker", old.AddSeconds(10));
        var recentJob = NewJob(now.AddDays(-1), stepId: "recent");
        recentJob.Claim("worker", now.AddDays(-1), now.AddDays(-1).AddMinutes(1));
        recentJob.Complete("worker", now.AddDays(-1).AddSeconds(10));

        var oldLease = new DistributedLease("old", "owner", old, old.AddMinutes(1));
        var activeLease = new DistributedLease("active", "owner", now, now.AddMinutes(10));
        var oldUsage = new HostedLlmDailyUsage(
            Guid.NewGuid(),
            DateOnly.FromDateTime(old),
            "old-model",
            old);
        var recentUsage = new HostedLlmDailyUsage(
            Guid.NewGuid(),
            DateOnly.FromDateTime(now),
            "new-model",
            now);
        var oldExecution = NewExecution(old);
        oldExecution.Complete(true, old.AddSeconds(1), null, null, null, 1, 1, "Approve", 1, false, false, null);
        var recentExecution = NewExecution(now.AddDays(-1));
        recentExecution.Complete(true, now.AddDays(-1).AddSeconds(1), null, null, null, 1, 1, "Approve", 1, false, false, null);

        db.AddRange(
            oldJob,
            recentJob,
            oldLease,
            activeLease,
            oldUsage,
            recentUsage,
            oldExecution,
            recentExecution);
        await db.SaveChangesAsync();

        var cleanup = new AgentPersistenceCleanupService(
            db,
            new AgentPersistenceRetentionOptions
            {
                TerminalJobDays = 14,
                ExpiredLeaseGraceMinutes = 0,
                UsageDays = 35,
                ExecutionDays = 90,
                BatchSize = 2
            },
            clock);

        var result = await cleanup.CleanupAsync();

        Assert.Equal(new AgentPersistenceCleanupResult(1, 1, 1, 1), result);
        Assert.Single(await db.AgentTaskJobs.ToListAsync());
        Assert.Single(await db.DistributedLeases.ToListAsync());
        Assert.Single(await db.HostedLlmDailyUsages.ToListAsync());
        Assert.Single(await db.AgentExecutionRecords.ToListAsync());
    }

    private static FlowOSDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"agent-persistence-{Guid.NewGuid():N}")
            .Options;
        return new FlowOSDbContext(options);
    }

    private static AgentTaskJob NewJob(
        DateTime now,
        string stepId = "review",
        int maxAttempts = 3)
    {
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        return new AgentTaskJob(
            tenantId,
            instanceId,
            stepId,
            "risk-agent",
            "Choose the next legal event.",
            true,
            true,
            AgentTaskSource.WorkflowEntry,
            AgentTaskJob.CreateActiveKey(tenantId, instanceId, stepId),
            now,
            now,
            maxAttempts);
    }

    private static AgentTaskEnqueueRequest NewRequest(
        string stepId = "review",
        int maxAttempts = 3) =>
        new(
            TenantId: TestIds.TenantId,
            WorkflowInstanceId: TestIds.WorkflowInstanceId,
            StepId: stepId,
            RequestedAgentId: "risk-agent",
            Objective: "Choose the next legal event.",
            AllowAutoCommit: true,
            RequireAgentActor: true,
            Source: AgentTaskSource.WorkflowEntry,
            MaxAttempts: maxAttempts);

    private static AgentExecutionRecord NewExecution(DateTime startedAtUtc) =>
        new(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "review",
            "Agent:risk",
            AgentExecutionMode.Suggest,
            startedAtUtc);

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 12, 0, 0, DateTimeKind.Utc);

    private static class TestIds
    {
        public static readonly Guid TenantId = Guid.NewGuid();
        public static readonly Guid WorkflowInstanceId = Guid.NewGuid();
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTime utcNow)
        {
            _utcNow = new DateTimeOffset(utcNow);
        }

        public DateTime UtcNow => _utcNow.UtcDateTime;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
