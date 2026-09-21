using FlowOS.Agents.Abstractions;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.Handlers;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Infrastructure.Services;
using FlowOS.Security.Interfaces;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FlowOS.UnitTests.Agents;

public sealed class DurableAgentRuntimeTests
{
    [Fact]
    public async Task WorkflowEntry_StagesOneAgentJob_WithoutCallingProvider()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var definition = AgentDefinition(tenantId, "AgentReview");
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var queue = new AgentTaskQueue(db);
        var handler = new WorkflowCommandHandlers(
            new UnitOfWork(db),
            Mock.Of<IEventRegistry>(),
            Mock.Of<ICurrentUser>(),
            Mock.Of<ICapabilityService>(),
            new WorkflowEngine(new StateMachineEngine()),
            agentTaskQueue: queue);

        var instanceId = await handler.Handle(
            new StartWorkflowCommand(
                tenantId,
                definition.Id,
                null,
                null,
                Guid.Empty,
                null,
                null),
            CancellationToken.None);

        var job = Assert.Single(await db.AgentTaskJobs.ToListAsync());
        Assert.Equal(instanceId, job.WorkflowInstanceId);
        Assert.Equal("AgentReview", job.StepId);
        Assert.Equal(AgentTaskJobStatus.Pending, job.Status);
        Assert.Empty(await db.AgentExecutionRecords.ToListAsync());
    }

    [Fact]
    public async Task WorkflowEntry_DoesNotStageExplicitHumanTask()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(
            tenantId,
            "HumanOnly",
            1,
            "HumanReview");
        definition.AddStep(new WorkflowStepDefinition(
            "HumanReview",
            WorkflowStepType.HumanTask)
        {
            Actor = StepActor.Human,
            NextSteps = new Dictionary<string, string>
            {
                ["APPROVE"] = "END"
            }
        });
        definition.Publish();
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var handler = new WorkflowCommandHandlers(
            new UnitOfWork(db),
            Mock.Of<IEventRegistry>(),
            Mock.Of<ICurrentUser>(),
            Mock.Of<ICapabilityService>(),
            new WorkflowEngine(new StateMachineEngine()),
            agentTaskQueue: new AgentTaskQueue(db));

        await handler.Handle(
            new StartWorkflowCommand(
                tenantId,
                definition.Id,
                null,
                null,
                Guid.Empty,
                null,
                null),
            CancellationToken.None);

        Assert.Empty(await db.AgentTaskJobs.ToListAsync());
    }

    [Fact]
    public async Task WorkflowEntry_StagesAgentCommand_WithoutCallingProvider()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(
            tenantId,
            "AgentCommand",
            1,
            "AgentReview");
        definition.AddStep(new WorkflowStepDefinition(
            "AgentReview",
            WorkflowStepType.Command)
        {
            Actor = StepActor.Agent,
            AgentProvider = AgentProviderKinds.FlowosRisk,
            NextSteps = new Dictionary<string, string>
            {
                ["APPROVE"] = "END"
            }
        });
        definition.Publish();
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var handler = new WorkflowCommandHandlers(
            new UnitOfWork(db),
            Mock.Of<IEventRegistry>(),
            Mock.Of<ICurrentUser>(),
            Mock.Of<ICapabilityService>(),
            new WorkflowEngine(new StateMachineEngine()),
            agentTaskQueue: new AgentTaskQueue(db));

        var instanceId = await handler.Handle(
            new StartWorkflowCommand(
                tenantId,
                definition.Id,
                null,
                null,
                Guid.Empty,
                null,
                null),
            CancellationToken.None);

        var job = Assert.Single(await db.AgentTaskJobs.ToListAsync());
        Assert.Equal(instanceId, job.WorkflowInstanceId);
        Assert.Equal("AgentReview", job.StepId);
        Assert.Equal(AgentTaskJobStatus.Pending, job.Status);
        Assert.Empty(await db.AgentExecutionRecords.ToListAsync());
    }

    [Fact]
    public async Task Coordinator_CompletesJob_AndChainsNextAgentStep()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(UtcNow());
        var tenantId = Guid.NewGuid();
        var definition = AgentDefinition(tenantId, "AgentOne", "AgentTwo");
        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.Empty,
            definition.Version,
            "AgentOne");
        instance.Wait();
        db.AddRange(definition, instance);
        await db.SaveChangesAsync();

        var queue = new AgentTaskQueue(db, clock);
        var runner = new Mock<IAgentTaskRunner>();
        runner
            .Setup(x => x.TryRunForCurrentStepAsync(
                tenantId,
                instance.Id,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<AgentTaskExecutionContext?>()))
            .Returns(async () =>
            {
                instance.AdvanceTo("AgentTwo");
                instance.Wait();
                await db.SaveChangesAsync();
                return SuccessfulRun(autoCommitted: true);
            });
        var coordinator = Coordinator(db, queue, runner.Object, clock);

        var enqueued = await queue.EnqueueAsync(Request(tenantId, instance.Id, "AgentOne"));
        var claim = await queue.ClaimAsync(
            enqueued.JobId,
            "worker-a",
            TimeSpan.FromMinutes(1));
        Assert.NotNull(claim);

        var processed = await coordinator.ProcessClaimAsync(claim!);

        Assert.Equal(AgentTaskJobStatus.Completed, processed.Status);
        var jobs = await db.AgentTaskJobs.OrderBy(x => x.RequestedAtUtc).ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.Contains(jobs, x => x.Id == enqueued.JobId && x.Status == AgentTaskJobStatus.Completed);
        Assert.Contains(jobs, x => x.StepId == "AgentTwo" && x.Status == AgentTaskJobStatus.Pending);
    }

    [Fact]
    public async Task Queue_DeduplicatesOneActiveJob_AndSameStepCommitReschedules()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(UtcNow());
        var tenantId = Guid.NewGuid();
        var definition = AgentDefinition(tenantId, "AgentLoop");
        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.Empty,
            definition.Version,
            "AgentLoop");
        instance.Wait();
        db.AddRange(definition, instance);
        await db.SaveChangesAsync();

        var queue = new AgentTaskQueue(db, clock);
        var request = Request(tenantId, instance.Id, "AgentLoop");
        var first = await queue.EnqueueAsync(request);
        var duplicate = await queue.EnqueueAsync(request);

        Assert.True(first.Created);
        Assert.False(duplicate.Created);
        Assert.Equal(first.JobId, duplicate.JobId);

        var runner = new Mock<IAgentTaskRunner>();
        runner
            .Setup(x => x.TryRunForCurrentStepAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<AgentTaskExecutionContext?>()))
            .ReturnsAsync(SuccessfulRun(autoCommitted: true));
        var coordinator = Coordinator(db, queue, runner.Object, clock);
        var claim = await queue.ClaimAsync(
            first.JobId,
            "worker-loop",
            TimeSpan.FromMinutes(1));
        Assert.NotNull(claim);

        await coordinator.ProcessClaimAsync(claim!);

        var jobs = await db.AgentTaskJobs.ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.Single(jobs, x => x.Status == AgentTaskJobStatus.Completed);
        Assert.Single(jobs, x =>
            x.Status == AgentTaskJobStatus.Pending &&
            x.StepId == "AgentLoop");
    }

    [Fact]
    public async Task SynchronousCoordinator_ReconcilesExpiredClaim_ThenClaimsDirectly()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(UtcNow());
        var tenantId = Guid.NewGuid();
        var definition = AgentDefinition(tenantId, "AgentReview");
        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.Empty,
            definition.Version,
            "AgentReview");
        instance.Wait();
        db.AddRange(definition, instance);
        await db.SaveChangesAsync();

        var queue = new AgentTaskQueue(db, clock);
        var request = Request(tenantId, instance.Id, "AgentReview");
        var existing = await queue.EnqueueAsync(request);
        Assert.NotNull(await queue.ClaimAsync(
            existing.JobId,
            "stale-worker",
            TimeSpan.FromSeconds(1)));
        clock.Advance(TimeSpan.FromSeconds(2));

        var runner = new Mock<IAgentTaskRunner>();
        runner
            .Setup(x => x.TryRunForCurrentStepAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<AgentTaskExecutionContext?>()))
            .ReturnsAsync(SuccessfulRun(autoCommitted: false));
        var coordinator = Coordinator(db, queue, runner.Object, clock);

        var result = await coordinator.EnqueueAndExecuteAsync(
            request,
            "mcp-sync",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMinutes(1));

        Assert.Equal(existing.JobId, result.JobId);
        Assert.Equal(AgentTaskJobStatus.Completed, result.Status);
        Assert.NotNull(result.RunResult);
        runner.Verify(x => x.TryRunForCurrentStepAsync(
            tenantId,
            instance.Id,
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<AgentTaskExecutionContext?>()), Times.Once);
    }

    [Fact]
    public async Task Coordinator_RetriesTransientFailure_AndDeadLettersInvalidOutput()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(UtcNow());
        var tenantId = Guid.NewGuid();
        var definition = AgentDefinition(tenantId, "AgentReview");
        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.Empty,
            definition.Version,
            "AgentReview");
        instance.Wait();
        db.AddRange(definition, instance);
        await db.SaveChangesAsync();

        var queue = new AgentTaskQueue(db, clock);
        var runner = new Mock<IAgentTaskRunner>();
        runner
            .SetupSequence(x => x.TryRunForCurrentStepAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<AgentTaskExecutionContext?>()))
            .ReturnsAsync(FailedRun(
                AgentFailureCodes.ProviderRateLimit,
                "Provider rate limit was reached."))
            .ReturnsAsync(FailedRun(
                AgentFailureCodes.InvalidModelOutput,
                "Provider response did not match the agent contract."));
        var coordinator = Coordinator(db, queue, runner.Object, clock);
        var enqueued = await queue.EnqueueAsync(
            Request(tenantId, instance.Id, "AgentReview", maxAttempts: 3));

        var firstClaim = await queue.ClaimAsync(
            enqueued.JobId,
            "worker-retry",
            TimeSpan.FromMinutes(1));
        Assert.NotNull(firstClaim);
        var first = await coordinator.ProcessClaimAsync(firstClaim!);
        Assert.Equal(AgentTaskJobStatus.RetryScheduled, first.Status);

        clock.Advance(TimeSpan.FromSeconds(2));
        var secondClaim = await queue.ClaimAsync(
            enqueued.JobId,
            "worker-retry",
            TimeSpan.FromMinutes(1));
        Assert.NotNull(secondClaim);
        var second = await coordinator.ProcessClaimAsync(secondClaim!);

        Assert.Equal(AgentTaskJobStatus.DeadLettered, second.Status);
        var persisted = await queue.GetAsync(enqueued.JobId);
        Assert.Equal(AgentTaskJobStatus.DeadLettered, persisted?.Status);
        Assert.DoesNotContain("\r", persisted?.LastError ?? string.Empty);
    }

    [Fact]
    public async Task Coordinator_ReleasesCancelledClaimForRetry()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(UtcNow());
        var tenantId = Guid.NewGuid();
        var definition = AgentDefinition(tenantId, "AgentReview");
        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.Empty,
            definition.Version,
            "AgentReview");
        instance.Wait();
        db.AddRange(definition, instance);
        await db.SaveChangesAsync();

        var queue = new AgentTaskQueue(db, clock);
        var runner = new Mock<IAgentTaskRunner>();
        runner
            .Setup(x => x.TryRunForCurrentStepAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<AgentTaskExecutionContext?>()))
            .ThrowsAsync(new OperationCanceledException());
        var coordinator = Coordinator(db, queue, runner.Object, clock);
        var enqueued = await queue.EnqueueAsync(
            Request(tenantId, instance.Id, "AgentReview"));
        var claim = await queue.ClaimAsync(
            enqueued.JobId,
            "worker-shutdown",
            TimeSpan.FromMinutes(1));
        Assert.NotNull(claim);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => coordinator.ProcessClaimAsync(claim!, cancellation.Token));

        var persisted = await queue.GetAsync(enqueued.JobId);
        Assert.Equal(AgentTaskJobStatus.RetryScheduled, persisted?.Status);
        Assert.Null(persisted?.Claimant);
    }

    [Fact]
    public async Task TwoWorkers_CanOnlyDispatchOneActiveJob()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var queue = new AgentTaskQueue(db);
        var enqueued = await queue.EnqueueAsync(
            Request(tenantId, Guid.NewGuid(), "AgentReview"));

        var first = await queue.ClaimAsync(
            enqueued.JobId,
            "worker-one",
            TimeSpan.FromMinutes(1));
        var second = await queue.ClaimAsync(
            enqueued.JobId,
            "worker-two",
            TimeSpan.FromMinutes(1));

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Queue_RejectsCancellationFromExpiredClaimOwner()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(UtcNow());
        var queue = new AgentTaskQueue(db, clock);
        var enqueued = await queue.EnqueueAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), "AgentReview"));
        var expired = await queue.ClaimAsync(
            enqueued.JobId,
            "expired-worker",
            TimeSpan.FromSeconds(1));
        Assert.NotNull(expired);

        clock.Advance(TimeSpan.FromSeconds(2));
        var replacement = await queue.ClaimAsync(
            enqueued.JobId,
            "replacement-worker",
            TimeSpan.FromMinutes(1));
        Assert.NotNull(replacement);

        Assert.False(await queue.CancelAsync(
            enqueued.JobId,
            expired!.Claimant,
            "Stale worker cancellation."));
        var stillClaimed = await queue.GetAsync(enqueued.JobId);
        Assert.Equal(AgentTaskJobStatus.Claimed, stillClaimed?.Status);
        Assert.Equal(replacement!.Claimant, stillClaimed?.Claimant);

        Assert.True(await queue.CancelAsync(
            enqueued.JobId,
            replacement.Claimant,
            "Current step is no longer waiting."));
        Assert.Equal(
            AgentTaskJobStatus.Cancelled,
            (await queue.GetAsync(enqueued.JobId))?.Status);
    }

    [Fact]
    public async Task ReclaimedJob_ReusesCommittedExecution_WithoutDuplicateEvent()
    {
        await using var db = CreateDb();
        var clock = new MutableTimeProvider(UtcNow());
        var tenantId = Guid.NewGuid();
        var definition = AgentDefinition(tenantId, "AgentLoop");
        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.Empty,
            definition.Version,
            "AgentLoop");
        instance.Wait();
        db.AddRange(definition, instance);
        await db.SaveChangesAsync();

        var queue = new AgentTaskQueue(db, clock);
        var enqueued = await queue.EnqueueAsync(
            Request(tenantId, instance.Id, "AgentLoop"));
        Assert.NotNull(await queue.ClaimAsync(
            enqueued.JobId,
            "worker-before-crash",
            TimeSpan.FromSeconds(1)));

        var executionStore = new AgentExecutionStore(db, clock);
        var executionId = await executionStore.StartAsync(
            new AgentExecutionStartRequest(
                tenantId,
                instance.Id,
                "AgentLoop",
                "Agent:flowos-risk",
                AgentExecutionMode.Live,
                JobId: enqueued.JobId,
                Claimant: "worker-before-crash",
                IdempotencyKey: $"agent-job:{enqueued.JobId:N}:attempt:1"));
        await executionStore.CompleteAsync(
            executionId,
            new AgentExecutionCompletion(
                true,
                SuggestedEvent: "APPROVE",
                Confidence: 1,
                WasCommitted: true));

        clock.Advance(TimeSpan.FromSeconds(2));
        var reclaimed = await queue.ClaimAsync(
            enqueued.JobId,
            "worker-after-crash",
            TimeSpan.FromMinutes(1));
        Assert.NotNull(reclaimed);

        var runner = new Mock<IAgentTaskRunner>();
        var coordinator = new AgentTaskCoordinator(
            queue,
            runner.Object,
            new UnitOfWork(db),
            executionStore,
            clock);
        var processed = await coordinator.ProcessClaimAsync(reclaimed!);

        Assert.Equal(AgentTaskJobStatus.Completed, processed.Status);
        Assert.True(processed.RunResult?.AutoCommitted);
        runner.VerifyNoOtherCalls();
        Assert.Single(
            await db.AgentTaskJobs.Where(x =>
                x.Id == enqueued.JobId &&
                x.Status == AgentTaskJobStatus.Completed).ToListAsync());
    }

    private static AgentTaskCoordinator Coordinator(
        FlowOSDbContext db,
        IAgentTaskQueue queue,
        IAgentTaskRunner runner,
        TimeProvider clock) =>
        new(
            queue,
            runner,
            new UnitOfWork(db),
            Mock.Of<IAgentExecutionHistoryStore>(),
            clock);

    private static FlowOSDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"durable-agent-{Guid.NewGuid():N}")
            .Options;
        return new FlowOSDbContext(options);
    }

    private static WorkflowDefinition AgentDefinition(
        Guid tenantId,
        params string[] stepIds)
    {
        var definition = new WorkflowDefinition(
            tenantId,
            $"DurableAgent{Guid.NewGuid():N}",
            1,
            stepIds[0]);
        foreach (var stepId in stepIds)
        {
            definition.AddStep(new WorkflowStepDefinition(
                stepId,
                WorkflowStepType.HumanTask)
            {
                Actor = StepActor.Agent,
                AgentProvider = AgentProviderKinds.FlowosRisk,
                DecisionGuideline = "Choose the next legal event.",
                NextSteps = new Dictionary<string, string>
                {
                    ["APPROVE"] = "END"
                }
            });
        }
        definition.Publish();
        return definition;
    }

    private static AgentTaskEnqueueRequest Request(
        Guid tenantId,
        Guid instanceId,
        string stepId,
        int maxAttempts = 5) =>
        new(
            tenantId,
            instanceId,
            stepId,
            AgentProviderKinds.FlowosRisk,
            "Choose the next legal event.",
            AllowAutoCommit: true,
            RequireAgentActor: true,
            AgentTaskSource.WorkflowEntry,
            MaxAttempts: maxAttempts);

    private static AgentTaskRunResult SuccessfulRun(bool autoCommitted) =>
        new(
            true,
            autoCommitted,
            null,
            null,
            AgentProviderKinds.FlowosRisk,
            AgentResult.WithActions(
                "Approved.",
                [new SuggestedAction("APPROVE", "Approved.", 1)]),
            null);

    private static AgentTaskRunResult FailedRun(string code, string reason) =>
        new(
            true,
            false,
            null,
            reason,
            AgentProviderKinds.FlowosRisk,
            AgentResult.Failure(code, reason),
            null);

    private static DateTime UtcNow() =>
        new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private long _timestamp;

        public MutableTimeProvider(DateTime utcNow)
        {
            _utcNow = new DateTimeOffset(utcNow);
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
            _timestamp += duration.Ticks;
        }
    }
}
