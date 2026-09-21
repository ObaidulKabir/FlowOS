using FlowOS.Agents.Abstractions;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.Services;
using FlowOS.Domain.Enums;
using FlowOS.Events.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using MediatR;
using Moq;

namespace FlowOS.UnitTests.Agents;

public sealed class AgentTaskRunnerProviderTests
{
    [Fact]
    public async Task Runner_UsesResolvedActor_AndRecordsSanitizedProviderFailure()
    {
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(
            tenantId,
            "ProviderFailure",
            3,
            "AgentReview");
        definition.AddStep(new WorkflowStepDefinition("AgentReview", WorkflowStepType.HumanTask)
        {
            Actor = "Agent",
            NextSteps = new Dictionary<string, string>
            {
                ["APPROVE"] = "END"
            }
        });
        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.NewGuid(),
            definition.Version,
            "AgentReview");
        instance.Wait();
        var packet = Packet(tenantId, instance.Id);

        var packetBuilder = new Mock<IDecisionPacketBuilder>();
        packetBuilder
            .Setup(x => x.BuildAsync(
                tenantId,
                instance.Id,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(packet);

        var instances = new Mock<IWorkflowInstanceRepository>();
        instances
            .Setup(x => x.GetByIdAsNoTrackingAsync(
                instance.Id,
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        var definitions = new Mock<IWorkflowDefinitionRepository>();
        definitions
            .Setup(x => x.GetByIdAsNoTrackingAsync(
                definition.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        var events = new Mock<IDomainEventRepository>();
        events
            .Setup(x => x.ListByCorrelationIdAsync(
                instance.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainEvent>());
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(x => x.WorkflowInstances).Returns(instances.Object);
        unitOfWork.SetupGet(x => x.WorkflowDefinitions).Returns(definitions.Object);
        unitOfWork.SetupGet(x => x.Events).Returns(events.Object);

        var failure = AgentResult.Failure(
            AgentFailureCodes.ProviderRateLimit,
            "Provider rate limit was reached.",
            new AgentTelemetry(429, "req_safe", 8, 2, 10, 2));
        var factory = new Mock<IWorkflowAgentFactory>();
        factory
            .Setup(x => x.ResolveAsync(
                packet,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedWorkflowAgent(
                new StubAgent(failure),
                "quote-llm",
                "TenantLlmWorkflowAgent",
                "quote-llm",
                "openai",
                "gpt-test"));

        AgentExecutionStartRequest? recordedStart = null;
        AgentExecutionCompletion? recordedCompletion = null;
        var executionId = Guid.NewGuid();
        var recorder = new Mock<IAgentExecutionRecorder>();
        recorder
            .Setup(x => x.StartAsync(
                It.IsAny<AgentExecutionStartRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<AgentExecutionStartRequest, CancellationToken>(
                (request, _) => recordedStart = request)
            .ReturnsAsync(executionId);
        recorder
            .Setup(x => x.CompleteAsync(
                executionId,
                It.IsAny<AgentExecutionCompletion>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, AgentExecutionCompletion, CancellationToken>(
                (_, completion, _) => recordedCompletion = completion)
            .ReturnsAsync(true);
        var lease = new Mock<IDistributedLeaseService>();
        lease
            .Setup(x => x.TryAcquireAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string owner, TimeSpan duration, CancellationToken _) =>
                new DistributedLeaseHandle(
                    key,
                    owner,
                    DateTime.UtcNow,
                    DateTime.UtcNow.Add(duration)));
        lease
            .Setup(x => x.ReleaseAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var mediator = new Mock<IMediator>();
        var runner = new AgentTaskRunner(
            packetBuilder.Object,
            mediator.Object,
            unitOfWork.Object,
            lease.Object,
            agentFactory: factory.Object,
            executionRecorder: recorder.Object);
        var jobId = Guid.NewGuid();

        var run = await runner.TryRunForCurrentStepAsync(
            tenantId,
            instance.Id,
            cancellationToken: CancellationToken.None,
            executionContext: new AgentTaskExecutionContext(
                jobId,
                "worker-provider",
                2,
                "AgentReview"));

        Assert.True(run.Ran);
        Assert.False(run.AutoCommitted);
        Assert.Equal("quote-llm", run.AgentId);
        Assert.Equal("quote-llm", run.ProviderAlias);
        Assert.Equal("openai", run.ProviderName);
        Assert.Equal("Agent:quote-llm", recordedStart?.Actor);
        Assert.Equal(AgentExecutionMode.Live, recordedStart?.Mode);
        Assert.Equal(jobId, recordedStart?.JobId);
        Assert.Equal("worker-provider", recordedStart?.Claimant);
        Assert.Equal($"agent-job:{jobId:N}:attempt:2", recordedStart?.IdempotencyKey);
        Assert.NotNull(recordedCompletion);
        Assert.False(recordedCompletion!.Success);
        Assert.Equal(AgentFailureCodes.ProviderRateLimit, recordedCompletion.FailureCode);
        Assert.Equal(429, recordedCompletion.HttpStatusCode);
        Assert.Equal(8, recordedCompletion.InputTokens);
        Assert.Equal(2, recordedCompletion.OutputTokens);
        mediator.Verify(
            x => x.Send(
                It.IsAny<PublishAgentInsightCommand>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HostedQuotaResolutionFailure_UsesDistinctCode_AndSuppressesInsight()
    {
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(
            tenantId,
            "HostedQuota",
            1,
            "AgentReview");
        definition.AddStep(new WorkflowStepDefinition(
            "AgentReview",
            WorkflowStepType.HumanTask)
        {
            Actor = "Agent",
            NextSteps = new Dictionary<string, string>
            {
                ["APPROVE"] = "END"
            }
        });
        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.NewGuid(),
            definition.Version,
            "AgentReview");
        instance.Wait();
        var packet = Packet(tenantId, instance.Id);

        var packetBuilder = new Mock<IDecisionPacketBuilder>();
        packetBuilder
            .Setup(builder => builder.BuildAsync(
                tenantId,
                instance.Id,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(packet);
        var instances = new Mock<IWorkflowInstanceRepository>();
        instances
            .Setup(repository => repository.GetByIdAsNoTrackingAsync(
                instance.Id,
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        var definitions = new Mock<IWorkflowDefinitionRepository>();
        definitions
            .Setup(repository => repository.GetByIdAsNoTrackingAsync(
                definition.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(work => work.WorkflowInstances).Returns(instances.Object);
        unitOfWork.SetupGet(work => work.WorkflowDefinitions).Returns(definitions.Object);

        var factory = new Mock<IWorkflowAgentFactory>();
        factory
            .Setup(value => value.ResolveAsync(
                packet,
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                $"{FlowOsHostedLlmCodes.Quota}: {FlowOsHostedLlmCodes.QuotaMessage}"));

        Guid? executionId = null;
        AgentExecutionCompletion? completion = null;
        var recorder = new Mock<IAgentExecutionRecorder>();
        recorder
            .Setup(value => value.StartAsync(
                It.IsAny<AgentExecutionStartRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<AgentExecutionStartRequest, CancellationToken>(
                (request, _) => executionId = request.ExecutionId)
            .ReturnsAsync((
                AgentExecutionStartRequest request,
                CancellationToken _) => request.ExecutionId!.Value);
        recorder
            .Setup(value => value.CompleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<AgentExecutionCompletion>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, AgentExecutionCompletion, CancellationToken>(
                (id, recorded, _) =>
                {
                    executionId = id;
                    completion = recorded;
                })
            .ReturnsAsync(true);

        var lease = new Mock<IDistributedLeaseService>();
        lease
            .Setup(value => value.TryAcquireAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DistributedLeaseHandle(
                "quota-test",
                "worker",
                DateTime.UtcNow,
                DateTime.UtcNow.AddMinutes(1)));
        lease
            .Setup(value => value.ReleaseAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var mediator = new Mock<IMediator>();
        var runner = new AgentTaskRunner(
            packetBuilder.Object,
            mediator.Object,
            unitOfWork.Object,
            lease.Object,
            agentFactory: factory.Object,
            executionRecorder: recorder.Object);

        var result = await runner.SuggestAsync(
            tenantId,
            instance.Id,
            cancellationToken: CancellationToken.None);

        Assert.False(result.Ran);
        Assert.Equal(executionId, result.ExecutionId);
        Assert.NotNull(completion);
        Assert.Equal(
            AgentFailureCodes.HostedQuotaDenied,
            completion!.FailureCode);
        Assert.False(completion.Success);
        mediator.Verify(
            value => value.Send(
                It.IsAny<PublishAgentInsightCommand>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DistributedLease_PreventsConcurrentProviderCalls()
    {
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(
            tenantId,
            "ConcurrentProvider",
            1,
            "AgentReview");
        definition.AddStep(new WorkflowStepDefinition(
            "AgentReview",
            WorkflowStepType.HumanTask)
        {
            Actor = "Agent",
            NextSteps = new Dictionary<string, string>
            {
                ["APPROVE"] = "END"
            }
        });
        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.Empty,
            definition.Version,
            "AgentReview");
        instance.Wait();
        var packet = Packet(tenantId, instance.Id);

        var packetBuilder = new Mock<IDecisionPacketBuilder>();
        packetBuilder
            .Setup(x => x.BuildAsync(
                tenantId,
                instance.Id,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(packet);
        var instances = new Mock<IWorkflowInstanceRepository>();
        instances
            .Setup(x => x.GetByIdAsNoTrackingAsync(
                instance.Id,
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        var definitions = new Mock<IWorkflowDefinitionRepository>();
        definitions
            .Setup(x => x.GetByIdAsNoTrackingAsync(
                definition.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        var events = new Mock<IDomainEventRepository>();
        events
            .Setup(x => x.ListByCorrelationIdAsync(
                instance.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainEvent>());
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(x => x.WorkflowInstances).Returns(instances.Object);
        unitOfWork.SetupGet(x => x.WorkflowDefinitions).Returns(definitions.Object);
        unitOfWork.SetupGet(x => x.Events).Returns(events.Object);

        var agent = new BlockingAgent();
        var factory = new Mock<IWorkflowAgentFactory>();
        factory
            .Setup(x => x.ResolveAsync(
                packet,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedWorkflowAgent(
                agent,
                "quote-llm",
                "TenantLlmWorkflowAgent",
                "quote-llm",
                "openai",
                "gpt-test"));
        var runner = new AgentTaskRunner(
            packetBuilder.Object,
            Mock.Of<IMediator>(),
            unitOfWork.Object,
            new ExclusiveLeaseService(),
            agentFactory: factory.Object);

        var firstTask = runner.SuggestAsync(tenantId, instance.Id);
        await agent.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await runner.SuggestAsync(tenantId, instance.Id);
        agent.Release.TrySetResult();
        var first = await firstTask;

        Assert.True(first.Ran);
        Assert.False(second.Ran);
        Assert.Contains("owned by another worker", second.SkipReason);
        Assert.Equal(1, agent.Calls);
    }

    private static DecisionPacket Packet(Guid tenantId, Guid instanceId) =>
        new(
            tenantId,
            instanceId,
            "AgentReview",
            "Waiting",
            "HumanTask",
            "Agent",
            null,
            null,
            new Dictionary<string, object?>(),
            ["APPROVE"],
            ["APPROVE"],
            Array.Empty<string>(),
            Array.Empty<SlaReminderFact>(),
            null,
            new Dictionary<string, object>(),
            "Decide",
            new AutoCommitPolicy(0.8, ["APPROVE"]),
            new AgentProviderRef("quote-llm", "openai", "gpt-test", null, true));

    private sealed class StubAgent : IWorkflowAgent
    {
        private readonly AgentResult _result;

        public StubAgent(AgentResult result) => _result = result;

        public Task<AgentResult> ExecuteAsync(AgentContext context) =>
            Task.FromResult(_result);

        public Task<AgentResult> ExecuteAsync(
            AgentContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }

    private sealed class BlockingAgent : IWorkflowAgent
    {
        private int _calls;

        public int Calls => _calls;
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AgentResult> ExecuteAsync(AgentContext context) =>
            ExecuteAsync(context, CancellationToken.None);

        public async Task<AgentResult> ExecuteAsync(
            AgentContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return AgentResult.FromInsight("Completed.");
        }
    }

    private sealed class ExclusiveLeaseService : IDistributedLeaseService
    {
        private int _held;
        private string? _owner;

        public Task<DistributedLeaseHandle?> TryAcquireAsync(
            string leaseKey,
            string ownerId,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref _held, 1, 0) != 0)
                return Task.FromResult<DistributedLeaseHandle?>(null);

            _owner = ownerId;
            var now = DateTime.UtcNow;
            return Task.FromResult<DistributedLeaseHandle?>(
                new DistributedLeaseHandle(
                    leaseKey,
                    ownerId,
                    now,
                    now.Add(duration)));
        }

        public Task<bool> ReleaseAsync(
            string leaseKey,
            string ownerId,
            CancellationToken cancellationToken = default)
        {
            var released = string.Equals(
                _owner,
                ownerId,
                StringComparison.Ordinal);
            if (released)
            {
                _owner = null;
                Interlocked.Exchange(ref _held, 0);
            }
            return Task.FromResult(released);
        }
    }
}
